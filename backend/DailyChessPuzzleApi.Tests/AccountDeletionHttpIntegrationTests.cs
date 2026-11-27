using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
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

    [PostgresFact]
    public async Task Concurrent_account_deletion_preserves_data_integrity()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        using var seedFactory = new TestWebApplicationFactory(database);
        var login = await SeedLoginAsync(database, seedFactory);
        var other = await SeedLoginAsync(database, seedFactory, suffix: "other");
        var gate = new CountryUpdateRaceInterceptor();
        using var factory = new TestWebApplicationFactory(database, gate);
        using var client = factory.CreateClient();
        var responses = await Task.WhenAll(
            DeleteAsync(client, login.AccessToken, new { currentPassword = Password }),
            DeleteAsync(client, login.AccessToken, new { currentPassword = Password }))
            .WaitAsync(TimeSpan.FromSeconds(40));
        try
        {
            Assert.Equal(2, gate.ArrivedCount);
            Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
            await AssertDeletedRaceStateAsync(database, login.UserId, other.UserId);
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    [PostgresFact]
    public Task Account_deletion_DailyStart_DeletionFirst() =>
        RunDeletionPuzzleRaceAsync(RaceOperation.DailyStart, RaceOrder.DeletionFirst);

    [PostgresFact]
    public Task Account_deletion_DailyStart_PuzzleFirst() =>
        RunDeletionPuzzleRaceAsync(RaceOperation.DailyStart, RaceOrder.PuzzleFirst);

    [PostgresFact]
    public Task Account_deletion_DailyStart_Simultaneous() =>
        RunDeletionPuzzleRaceAsync(RaceOperation.DailyStart, RaceOrder.Simultaneous);

    [PostgresFact]
    public Task Account_deletion_DailyCompletion_DeletionFirst() =>
        RunDeletionPuzzleRaceAsync(RaceOperation.DailyCompletion, RaceOrder.DeletionFirst);

    [PostgresFact]
    public Task Account_deletion_DailyCompletion_PuzzleFirst() =>
        RunDeletionPuzzleRaceAsync(RaceOperation.DailyCompletion, RaceOrder.PuzzleFirst);

    [PostgresFact]
    public Task Account_deletion_DailyCompletion_Simultaneous() =>
        RunDeletionPuzzleRaceAsync(RaceOperation.DailyCompletion, RaceOrder.Simultaneous);

    [PostgresFact]
    public Task Account_deletion_BonusStart_DeletionFirst() =>
        RunDeletionPuzzleRaceAsync(RaceOperation.BonusStart, RaceOrder.DeletionFirst);

    [PostgresFact]
    public Task Account_deletion_BonusStart_PuzzleFirst() =>
        RunDeletionPuzzleRaceAsync(RaceOperation.BonusStart, RaceOrder.PuzzleFirst);

    [PostgresFact]
    public Task Account_deletion_BonusStart_Simultaneous() =>
        RunDeletionPuzzleRaceAsync(RaceOperation.BonusStart, RaceOrder.Simultaneous);

    [PostgresFact]
    public Task Account_deletion_BonusCompletion_DeletionFirst() =>
        RunDeletionPuzzleRaceAsync(RaceOperation.BonusCompletion, RaceOrder.DeletionFirst);

    [PostgresFact]
    public Task Account_deletion_BonusCompletion_PuzzleFirst() =>
        RunDeletionPuzzleRaceAsync(RaceOperation.BonusCompletion, RaceOrder.PuzzleFirst);

    [PostgresFact]
    public Task Account_deletion_BonusCompletion_Simultaneous() =>
        RunDeletionPuzzleRaceAsync(RaceOperation.BonusCompletion, RaceOrder.Simultaneous);

    private enum RaceOperation { DailyStart, DailyCompletion, BonusStart, BonusCompletion }
    private enum RaceOrder { DeletionFirst, PuzzleFirst, Simultaneous }

    private static async Task RunDeletionPuzzleRaceAsync(RaceOperation operation, RaceOrder order)
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await database.SeedDailyPuzzleAsync(PuzzleAudience.Registered);
        using var seedFactory = new TestWebApplicationFactory(database);
        using var seedClient = seedFactory.CreateClient();
        var login = await SeedLoginAsync(database, seedFactory);
        var other = await SeedLoginAsync(database, seedFactory, suffix: "other");
        await using (var context = database.CreateContext())
        {
            context.Puzzles.Add(new Puzzle
            {
                LichessPuzzleId = "deletion-race-bonus",
                Fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
                Moves = "e2e4 e7e5",
                Rating = 2600,
                Popularity = 100,
                Themes = "opening",
                IsActive = true
            });
            await context.SaveChangesAsync();
        }
        int sessionId = 0;
        if (operation != RaceOperation.DailyStart)
        {
            using var started = await PuzzlePostAsync(seedClient, login.AccessToken, "session/start");
            Assert.Equal(HttpStatusCode.OK, started.StatusCode);
            await using (var context = database.CreateContext())
                sessionId = await context.PuzzleGameSessions.Where(x => x.UserId == login.UserId).Select(x => x.Id).SingleAsync();
            if (operation != RaceOperation.DailyCompletion)
            {
                using var completed = await PuzzlePostAsync(seedClient, login.AccessToken, "move",
                    new { sessionId, uci = "e7e5", expectedMoveIndex = 1 });
                Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
                await using (var context = database.CreateContext())
                    Assert.True(await context.BonusPuzzleAssignments.AnyAsync(x => x.UserId == login.UserId));
                if (operation == RaceOperation.BonusCompletion)
                {
                    using var bonus = await PuzzlePostAsync(seedClient, login.AccessToken, "bonus/start");
                    Assert.Equal(HttpStatusCode.OK, bonus.StatusCode);
                    using var move = await PuzzlePostAsync(seedClient, login.AccessToken, "bonus/move",
                        new { uci = "e7e5", expectedMoveIndex = 1 });
                    Assert.Equal(HttpStatusCode.OK, move.StatusCode);
                    await using var context = database.CreateContext();
                    Assert.True(await context.BonusPuzzleAssignments.AnyAsync(x => x.UserId == login.UserId && x.CurrentMoveIndex == 2));
                }
            }
        }

        var gate = new DeletionPuzzleGate(operation);
        using var factory = new TestWebApplicationFactory(database, gate);
        using var client = factory.CreateClient();
        Task<HttpResponseMessage>? deletion = null;
        Task<HttpResponseMessage>? puzzle = null;
        try
        {
            deletion = DeleteAsync(client, login.AccessToken, new { currentPassword = Password });
            await gate.DeletionArrived.Task.WaitAsync(TimeSpan.FromSeconds(15));
            puzzle = operation switch
            {
                RaceOperation.DailyStart => PuzzlePostAsync(client, login.AccessToken, "session/start"),
                RaceOperation.DailyCompletion => PuzzlePostAsync(client, login.AccessToken, "move", new { sessionId, uci = "e7e5", expectedMoveIndex = 1 }),
                RaceOperation.BonusStart => PuzzlePostAsync(client, login.AccessToken, "bonus/start"),
                RaceOperation.BonusCompletion => PuzzlePostAsync(client, login.AccessToken, "bonus/submit"),
                _ => throw new InvalidOperationException()
            };
            await gate.PuzzleArrived.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.NotSame(gate.DeletionArrived.Task.Result, gate.PuzzleArrived.Task.Result);
            if (order == RaceOrder.DeletionFirst)
            {
                gate.ReleaseDeletion.TrySetResult(true);
                await deletion.WaitAsync(TimeSpan.FromSeconds(20));
            }
            else if (order == RaceOrder.PuzzleFirst)
            {
                gate.ReleasePuzzle.TrySetResult(true);
                await puzzle.WaitAsync(TimeSpan.FromSeconds(20));
            }
            gate.ReleaseDeletion.TrySetResult(true);
            gate.ReleasePuzzle.TrySetResult(true);
            await Task.WhenAll(deletion, puzzle).WaitAsync(TimeSpan.FromSeconds(30));
            var deleted = await deletion;
            var played = await puzzle;
            Assert.True(deleted.StatusCode == HttpStatusCode.OK,
                $"Deletion: {deleted.StatusCode}: {await deleted.Content.ReadAsStringAsync()}");
            var accepted = order == RaceOrder.PuzzleFirst
                ? played.StatusCode == HttpStatusCode.OK
                : played.StatusCode is HttpStatusCode.OK or HttpStatusCode.Unauthorized or HttpStatusCode.NotFound or HttpStatusCode.Conflict or HttpStatusCode.Forbidden;
            // A 500 (including FK violation/deadlock) is a regression, not an accepted race outcome.
            Assert.True(accepted, $"Puzzle: {played.StatusCode}: {await played.Content.ReadAsStringAsync()}");
            if (order == RaceOrder.DeletionFirst && operation == RaceOperation.DailyStart)
                Assert.NotEqual(HttpStatusCode.OK, played.StatusCode);
            await AssertDeletedRaceStateAsync(database, login.UserId, other.UserId);
            using var stale = await PuzzlePostAsync(client, login.AccessToken, "session/start");
            Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);
        }
        finally
        {
            gate.ReleaseDeletion.TrySetResult(true);
            gate.ReleasePuzzle.TrySetResult(true);
            // Drain requests before disposing the factory or test schema.
            foreach (var task in new[] { deletion, puzzle })
            {
                if (task == null) continue;
                try { (await task.WaitAsync(TimeSpan.FromSeconds(30))).Dispose(); }
                catch { /* Preserve the original test failure. */ }
            }
        }
    }

    private static async Task<HttpResponseMessage> PuzzlePostAsync(HttpClient client, string token, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/puzzle/{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body != null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private static async Task AssertDeletedRaceStateAsync(PostgresTestDatabase database, int userId, int otherId)
    {
        await using var context = database.CreateContext();
        var identity = $"user:{userId}";
        Assert.False(await context.Users.AnyAsync(x => x.Id == userId));
        Assert.False(await context.RefreshTokens.AnyAsync(x => x.UserId == userId));
        Assert.Empty(await context.PuzzleGameSessions.ToListAsync());
        Assert.Empty(await context.BonusPuzzleAssignments.ToListAsync());
        Assert.Empty(await context.PuzzleAttempts.ToListAsync());
        Assert.False(await context.LeaderboardScores.AnyAsync(x => x.UserId == userId || x.ClientId == identity));
        Assert.False(await context.LeaderboardCacheOutboxes.AnyAsync(x => x.UserId == userId || (x.ClientId == identity && x.PuzzleAttemptId != null)));
        Assert.False(await context.LeaderboardCacheOutboxDeliveries.AnyAsync(x => x.TargetUserId == userId || (x.TargetClientId == identity && !x.IsRemoval)));
        Assert.True(await context.Users.AnyAsync(x => x.Id == otherId));
        Assert.True(await context.RefreshTokens.AnyAsync(x => x.UserId == otherId));
    }

    private sealed class DeletionPuzzleGate(RaceOperation operation) : DbCommandInterceptor
    {
        public TaskCompletionSource<DbContext> DeletionArrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<DbContext> PuzzleArrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> ReleaseDeletion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> ReleasePuzzle { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context is not { } context) return result;
            var sql = command.CommandText;
            bool Has(string value) => sql.Contains(value, StringComparison.OrdinalIgnoreCase);
            var userLock = Has("\"Users\"") && Has("FOR UPDATE");
            if (userLock && DeletionArrived.TrySetResult(context))
                await ReleaseDeletion.Task.WaitAsync(TimeSpan.FromSeconds(60), cancellationToken);
            else if (!ReferenceEquals(context, DeletionArrived.Task.IsCompletedSuccessfully ? DeletionArrived.Task.Result : null))
            {
                var match = userLock || (operation switch
                {
                    RaceOperation.DailyStart => System.Text.RegularExpressions.Regex.IsMatch(sql,
                        @"^\s*SELECT\s+\w+\.""CountryCode""\s+FROM\s+""Users""", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
                    RaceOperation.DailyCompletion => Has("\"PuzzleGameSessions\"") && Has("FOR UPDATE"),
                    _ => Has("\"BonusPuzzleAssignments\"") && Has("FOR UPDATE")
                });
                if (match && PuzzleArrived.TrySetResult(context))
                    await ReleasePuzzle.Task.WaitAsync(TimeSpan.FromSeconds(60), cancellationToken);
            }
            return result;
        }
    }

}
