using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using DailyChessPuzzleApi.Services;
using Google.Apis.Auth;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DailyChessPuzzleApi.DTOs.Auth;
using DailyChessPuzzleApi.Models;
using DailyChessPuzzleApi.Services.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DailyChessPuzzleApi.Tests;

[Collection("Postgres integration")]
public sealed class AccountDeletionHttpIntegrationTests
{
    private const string Password = "a-strong-password";

    [PostgresFact]
    public async Task Anonymous_deletion_is_rejected()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        using var factory = new TestWebApplicationFactory(database);
        using var client = factory.CreateClient();
        using var response = await DeleteAsync(client, null, new { currentPassword = Password });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [PostgresFact]
    public async Task Password_account_requires_password_even_with_google_token()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        using var factory = new TestWebApplicationFactory(database);
        using var client = factory.CreateClient();
        var login = await SeedLoginAsync(database, factory, linkedGoogle: true);
        using var missing = await DeleteAsync(client, login.AccessToken, new { });
        await AssertErrorAsync(missing, HttpStatusCode.BadRequest, "CURRENT_PASSWORD_REQUIRED");
        using var bypass = await DeleteAsync(client, login.AccessToken, new { googleIdToken = "not-a-valid-token" });
        await AssertErrorAsync(bypass, HttpStatusCode.BadRequest, "CURRENT_PASSWORD_REQUIRED");
        using var wrong = await DeleteAsync(client, login.AccessToken, new { currentPassword = "wrong", googleIdToken = "not-a-valid-token" });
        await AssertErrorAsync(wrong, HttpStatusCode.Forbidden, "INVALID_CURRENT_PASSWORD");
        await using var context = database.CreateContext();
        Assert.True(await context.Users.AnyAsync(x => x.Id == login.UserId));
        Assert.True(await context.RefreshTokens.AnyAsync(x => x.UserId == login.UserId));
    }

    [PostgresFact]
    public async Task Correct_password_deletes_account_and_revokes_tokens_and_queues_removal()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        using var factory = new TestWebApplicationFactory(database);
        using var client = factory.CreateClient();
        var login = await SeedLoginAsync(database, factory, linkedGoogle: true);
        var other = await SeedLoginAsync(database, factory, suffix: "other");
        var clientId = $"user:{login.UserId}";
        await using (var context = database.CreateContext())
        {
            context.LeaderboardScores.AddRange(
                new LeaderboardScore { UserId = login.UserId, ClientId = clientId, Period = "alltime", PeriodKey = "alltime", TotalScore = 500m, SolvedCount = 1 },
                new LeaderboardScore { UserId = other.UserId, ClientId = $"user:{other.UserId}", Period = "alltime", PeriodKey = "alltime", TotalScore = 600m, SolvedCount = 1 });
            await context.SaveChangesAsync();
        }
        using var deleted = await DeleteAsync(client, login.AccessToken, new { currentPassword = Password });
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        await using (var context = database.CreateContext())
        {
            Assert.False(await context.Users.AnyAsync(x => x.Id == login.UserId));
            Assert.False(await context.RefreshTokens.AnyAsync(x => x.UserId == login.UserId));
            Assert.False(await context.LeaderboardScores.AnyAsync(x => x.ClientId == clientId));
            Assert.True(await context.Users.AnyAsync(x => x.Id == other.UserId));
            Assert.True(await context.LeaderboardScores.AnyAsync(x => x.UserId == other.UserId && x.TotalScore == 600m));
            var removal = Assert.Single(await context.LeaderboardCacheOutboxDeliveries.Where(x => x.TargetClientId == clientId).ToListAsync());
            Assert.True(removal.IsRemoval);
            Assert.Equal("alltime", removal.Period);
            Assert.Null(removal.TargetUserId);
        }
        using var oldAccess = await DeleteAsync(client, login.AccessToken, new { currentPassword = Password });
        Assert.Equal(HttpStatusCode.Unauthorized, oldAccess.StatusCode);
        using var oldRefresh = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = login.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, oldRefresh.StatusCode);
    }

    [PostgresFact]
    public async Task Sixth_deletion_request_is_rate_limited_without_blocking_other_user()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        using var factory = new TestWebApplicationFactory(database);
        using var client = factory.CreateClient();
        var login = await SeedLoginAsync(database, factory);
        var other = await SeedLoginAsync(database, factory, suffix: "other");
        for (var i = 0; i < 5; i++)
        {
            using var rejected = await DeleteAsync(client, login.AccessToken, new { currentPassword = "wrong" });
            await AssertErrorAsync(rejected, HttpStatusCode.Forbidden, "INVALID_CURRENT_PASSWORD");
        }
        using var limited = await DeleteAsync(client, login.AccessToken, new { currentPassword = Password });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        using var independent = await DeleteAsync(client, other.AccessToken, new { currentPassword = "wrong" });
        Assert.Equal(HttpStatusCode.Forbidden, independent.StatusCode);
        await using var context = database.CreateContext();
        Assert.True(await context.Users.AnyAsync(x => x.Id == login.UserId));
    }


    [PostgresFact]
    public async Task Google_only_deletion_requires_matching_recent_authentication()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        using var root = new TestWebApplicationFactory(database);
        using var factory = root.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IGoogleTokenValidator>();
                services.AddSingleton<IGoogleTokenValidator>(new DeletionGoogleValidator());
            }));
        using var client = factory.CreateClient();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var valid = GoogleProof("owner", now);
        await using var scope = factory.Services.CreateAsyncScope();
        var login = await scope.ServiceProvider.GetRequiredService<IAuthService>()
            .GoogleLoginAsync(new GoogleLoginRequest { IdToken = valid }, CancellationToken.None);
        Assert.False(login.HasPassword);

        using var missing = await DeleteAsync(client, login.AccessToken, new { currentPassword = Password });
        await AssertErrorAsync(missing, HttpStatusCode.BadRequest, "GOOGLE_REAUTH_REQUIRED");
        using var mismatch = await DeleteAsync(client, login.AccessToken, new { googleIdToken = GoogleProof("other", now) });
        await AssertErrorAsync(mismatch, HttpStatusCode.Forbidden, "GOOGLE_ACCOUNT_MISMATCH");
        using var stale = await DeleteAsync(client, login.AccessToken, new { googleIdToken = GoogleProof("owner", now - 600) });
        await AssertErrorAsync(stale, HttpStatusCode.Forbidden, "GOOGLE_REAUTH_REQUIRED");
        using var noTime = await DeleteAsync(client, login.AccessToken, new { googleIdToken = GoogleProof("owner", null) });
        await AssertErrorAsync(noTime, HttpStatusCode.Forbidden, "GOOGLE_REAUTH_REQUIRED");
        await using (var context = database.CreateContext())
            Assert.True(await context.Users.AnyAsync(x => x.Id == login.UserId));
        using var deleted = await DeleteAsync(client, login.AccessToken, new { googleIdToken = GoogleProof("owner", DateTimeOffset.UtcNow.ToUnixTimeSeconds()) });
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        await using (var context = database.CreateContext())
            Assert.False(await context.Users.AnyAsync(x => x.Id == login.UserId));
    }

    [PostgresRedisFact]
    public async Task Removal_clears_redis_notifies_signalr_and_ignores_late_update()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var redis = await RedisTestDatabase.CreateAsync();
        using var factory = new TestWebApplicationFactory(database, RedisTestDatabase.ConnectionString);
        using var client = factory.CreateClient();
        var login = await SeedLoginAsync(database, factory);
        var other = await SeedLoginAsync(database, factory, suffix: "other");
        var clientId = $"user:{login.UserId}";
        var otherId = $"user:{other.UserId}";
        var periodKey = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var key = redis.CreateKey("deletion:daily");
        var cache = redis.CreateCacheService();
        await cache.UpdateDailyScoreAsync(key, $"{key}:version", clientId, 500m, 1, 0);
        await cache.UpdateDailyScoreAsync(key, $"{key}:version", otherId, 600m, 1, 0);
        Assert.NotNull(await cache.GetUserScoreAsync(key, clientId));
        await using (var context = database.CreateContext())
        {
            context.LeaderboardScores.Add(new LeaderboardScore
            {
                UserId = login.UserId,
                ClientId = clientId,
                Period = "daily",
                PeriodKey = periodKey,
                TotalScore = 500m,
                SolvedCount = 1
            });
            await context.SaveChangesAsync();
        }

        var server = factory.Server;
        await using var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, "/hubs/leaderboard"), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
            }).Build();
        var notification = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = hub.On<JsonElement>("LeaderboardUpdated", payload =>
        {
            notification.TrySetResult(payload.Clone());
        });
        await hub.StartAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await hub.InvokeAsync("JoinLeaderboard", "daily", (string?)null).WaitAsync(TimeSpan.FromSeconds(10));
        using var deleted = await DeleteAsync(client, login.AccessToken, new { currentPassword = Password });
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        await using (var context = database.CreateContext())
        {
            var removal = await context.LeaderboardCacheOutboxDeliveries.SingleAsync(x => x.TargetClientId == clientId);
            Assert.True(removal.IsRemoval);
            removal.CacheKey = key; // Isolate Redis keys from other tests.
            await context.SaveChangesAsync();
        }
        await ProcessDeletionOutboxAsync(database, factory, cache);
        var message = await notification.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("daily", message.GetProperty("period").GetString());
        Assert.Equal(periodKey, message.GetProperty("periodKey").GetString());
        Assert.Null(await cache.GetUserScoreAsync(key, clientId));
        Assert.Equal((600m, 1), (await cache.GetUserScoreAsync(key, otherId))!.Value);

        await using (var context = database.CreateContext())
        {
            var late = new LeaderboardCacheOutbox { ClientId = clientId };
            late.Deliveries.Add(new LeaderboardCacheOutboxDelivery
            {
                TargetClientId = clientId,
                Period = "daily",
                PeriodKey = periodKey,
                CacheKey = key,
                TotalScore = 900m,
                SolvedCount = 1
            });
            context.LeaderboardCacheOutboxes.Add(late);
            await context.SaveChangesAsync();
        }
        await ProcessDeletionOutboxAsync(database, factory, cache);
        Assert.Null(await cache.GetUserScoreAsync(key, clientId));
        await using (var context = database.CreateContext())
        {
            Assert.False(await context.LeaderboardScores.AnyAsync(x => x.ClientId == clientId));
            var deliveries = await context.LeaderboardCacheOutboxDeliveries.Where(x => x.TargetClientId == clientId).ToListAsync();
            Assert.Equal(2, deliveries.Count);
            Assert.All(deliveries, x => Assert.Equal(LeaderboardCacheOutboxDeliveryStatus.Completed, x.Status));
        }
    }

    private static async Task ProcessDeletionOutboxAsync(PostgresTestDatabase database,
        TestWebApplicationFactory factory, ILeaderboardCacheService cache)
    {
        await using var context = database.CreateContext();
        var worker = new LeaderboardOutboxService(context, cache,
            NullLogger<LeaderboardOutboxService>.Instance,
            factory.Services.GetRequiredService<ILeaderboardPublisher>());
        Assert.False(await worker.ProcessPendingAsync(CancellationToken.None));
    }

    private static string GoogleProof(string subject, long? authTime)
    {
        var claims = new List<Claim> { new("sub", subject) };
        if (authTime.HasValue)
            claims.Add(new Claim("auth_time", authTime.Value.ToString(), ClaimValueTypes.Integer64));
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(claims: claims));
    }

    private sealed class DeletionGoogleValidator : IGoogleTokenValidator
    {
        public Task<GoogleJsonWebSignature.Payload> ValidateAsync(string idToken, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var token = new JwtSecurityTokenHandler().ReadJwtToken(idToken);
            return Task.FromResult(new GoogleJsonWebSignature.Payload
            {
                Subject = token.Subject,
                Email = $"{token.Subject}@example.test",
                EmailVerified = true
            });
        }
    }


    [PostgresFact]
    public async Task Archive_rows_are_deleted_only_for_target_user()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        using var factory = new TestWebApplicationFactory(database);
        using var client = factory.CreateClient();
        var login = await SeedLoginAsync(database, factory);
        var other = await SeedLoginAsync(database, factory, suffix: "other");
        await using var context = database.CreateContext();
        // Never reuse or drop an existing archive schema.
        await context.Database.ExecuteSqlRawAsync("CREATE SCHEMA scoring_archive");
        try
        {
            await context.Database.ExecuteSqlRawAsync("""
                CREATE TABLE scoring_archive."PuzzleAttempts" ("UserId" integer);
                CREATE TABLE scoring_archive."PuzzleGameSessions" ("UserId" integer);
                CREATE TABLE scoring_archive."BonusPuzzleAssignments" ("UserId" integer);
                CREATE TABLE scoring_archive."LeaderboardScores" ("UserId" integer);
                CREATE TABLE scoring_archive."LeaderboardCacheOutboxes" ("UserId" integer);
                CREATE TABLE scoring_archive."LeaderboardCacheOutboxDeliveries" ("TargetUserId" integer);
                """);
            await context.Database.ExecuteSqlAsync($"""
                INSERT INTO scoring_archive."PuzzleAttempts" VALUES ({login.UserId}), ({other.UserId});
                INSERT INTO scoring_archive."PuzzleGameSessions" VALUES ({login.UserId}), ({other.UserId});
                INSERT INTO scoring_archive."BonusPuzzleAssignments" VALUES ({login.UserId}), ({other.UserId});
                INSERT INTO scoring_archive."LeaderboardScores" VALUES ({login.UserId}), ({other.UserId});
                INSERT INTO scoring_archive."LeaderboardCacheOutboxes" VALUES ({login.UserId}), ({other.UserId});
                INSERT INTO scoring_archive."LeaderboardCacheOutboxDeliveries" VALUES ({login.UserId}), ({other.UserId});
                """);
            using var deleted = await DeleteAsync(client, login.AccessToken, new { currentPassword = Password });
            Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
            var remaining = await context.Database.SqlQuery<int>($"""
                SELECT "UserId" AS "Value" FROM scoring_archive."PuzzleAttempts"
                UNION ALL SELECT "UserId" FROM scoring_archive."PuzzleGameSessions"
                UNION ALL SELECT "UserId" FROM scoring_archive."BonusPuzzleAssignments"
                UNION ALL SELECT "UserId" FROM scoring_archive."LeaderboardScores"
                UNION ALL SELECT "UserId" FROM scoring_archive."LeaderboardCacheOutboxes"
                UNION ALL SELECT "TargetUserId" FROM scoring_archive."LeaderboardCacheOutboxDeliveries"
                """).ToListAsync();
            Assert.Equal(6, remaining.Count);
            Assert.All(remaining, id => Assert.Equal(other.UserId, id));
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync("DROP SCHEMA scoring_archive CASCADE");
        }
    }

    [PostgresFact]
    public async Task Failed_user_delete_rolls_back_scores_and_tokens()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        using var factory = new TestWebApplicationFactory(database);
        using var client = factory.CreateClient();
        var login = await SeedLoginAsync(database, factory);
        var clientId = $"user:{login.UserId}";
        await using (var context = database.CreateContext())
        {
            context.LeaderboardScores.Add(new LeaderboardScore
            {
                UserId = login.UserId,
                ClientId = clientId,
                Period = "alltime",
                PeriodKey = "alltime",
                TotalScore = 500m,
                SolvedCount = 1
            });
            await context.SaveChangesAsync();
            await context.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION reject_account_delete() RETURNS trigger
                LANGUAGE plpgsql AS $body$
                BEGIN
                    RAISE EXCEPTION 'forced account deletion failure';
                END;
                $body$;
                CREATE TRIGGER reject_account_delete BEFORE DELETE ON "Users"
                FOR EACH ROW EXECUTE FUNCTION reject_account_delete();
                """);
        }
        using var failed = await DeleteAsync(client, login.AccessToken, new { currentPassword = Password });
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        await using (var context = database.CreateContext())
        {
            Assert.True(await context.Users.AnyAsync(x => x.Id == login.UserId));
            Assert.True(await context.RefreshTokens.AnyAsync(x => x.UserId == login.UserId));
            Assert.True(await context.LeaderboardScores.AnyAsync(x => x.ClientId == clientId && x.TotalScore == 500m));
            Assert.False(await context.LeaderboardCacheOutboxDeliveries.AnyAsync(x => x.TargetClientId == clientId));
        }
    }

    private static async Task<AuthResponse> SeedLoginAsync(PostgresTestDatabase database, TestWebApplicationFactory factory, bool linkedGoogle = false, string suffix = "primary")
    {
        var email = $"deletion-{suffix}@example.test";
        await using (var context = database.CreateContext())
        {
            var user = new User { Username = $"deletion-{suffix}", FirstName = "Deletion", LastName = "Test", Email = email, EmailVerified = true, CreatedAtUtc = DateTime.UtcNow, GoogleId = linkedGoogle ? $"google-{suffix}" : null };
            user.PasswordHash = new PasswordHasher<User>().HashPassword(user, Password);
            context.Users.Add(user);
            await context.SaveChangesAsync();
        }
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IAuthService>().LoginAsync(new LoginRequest { Email = email, Password = Password }, CancellationToken.None);
    }

    private static async Task<HttpResponseMessage> DeleteAsync(HttpClient client, string? token, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, "/api/auth/account") { Content = JsonContent.Create(body) };
        if (token != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private static async Task AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, json.RootElement.GetProperty("errorCode").GetString());
    }
}
