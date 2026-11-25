using Google.Apis.Auth;
using System.IdentityModel.Tokens.Jwt;
using DailyChessPuzzleApi.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using DailyChessPuzzleApi.DTOs.Auth;
using DailyChessPuzzleApi.Exceptions;
using DailyChessPuzzleApi.Models;
using DailyChessPuzzleApi.Services;
using DailyChessPuzzleApi.Services.Interfaces;
using DailyChessPuzzleApi.Settings;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace DailyChessPuzzleApi.Tests;

[Collection("Postgres integration")]
public sealed class AuthServiceIntegrationTests
{
    [PostgresFact]
    public async Task Registration_verification_login_and_refresh_rotation_work_as_one_flow()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var emailSender = new RecordingEmailSender();
        var jwtSettings = new JwtSettings
        {
            Secret = new string('s', 64),
            Issuer = "DailyChessPuzzleApi.Tests",
            Audience = "DailyChessPuzzleClient.Tests",
            ExpirationMinutes = 60
        };
        var configuration = new ConfigurationBuilder().Build();
        var service = new AuthService(
            context,
            new PasswordHasher<User>(),
            Options.Create(jwtSettings),
            configuration,
            emailSender);

        var registration = await service.RegisterAsync(
            new RegisterRequest
            {
                Username = "auth-integration",
                FirstName = "Auth",
                LastName = "Test",
                Email = "auth@example.test",
                Password = "a-strong-password",
                CountryCode = "tr"
            },
            CancellationToken.None);

        Assert.True(registration.EmailVerificationRequired);
        Assert.Equal("auth@example.test", registration.Email);
        Assert.Matches(new Regex("^[0-9]{6}$"), emailSender.LastCode!);

        var unverifiedLogin = await Assert.ThrowsAsync<AppException>(() =>
            service.LoginAsync(
                new LoginRequest
                {
                    Email = "auth@example.test",
                    Password = "a-strong-password"
                },
                CancellationToken.None));
        Assert.Equal("EMAIL_NOT_VERIFIED", unverifiedLogin.ErrorCode);

        await service.VerifyEmailAsync(
            new EmailVerificationRequest
            {
                Email = "auth@example.test",
                Code = emailSender.LastCode!
            },
            CancellationToken.None);

        var login = await service.LoginAsync(
            new LoginRequest
            {
                Email = "auth@example.test",
                Password = "a-strong-password"
            },
            CancellationToken.None);

        Assert.Equal("TR", login.CountryCode);
        Assert.False(string.IsNullOrWhiteSpace(login.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(login.RefreshToken));


        var refresh = await service.RefreshAsync(
            new RefreshTokenRequest
            {
                RefreshToken = login.RefreshToken
            },
            CancellationToken.None);

        Assert.NotEqual(login.RefreshToken, refresh.RefreshToken);
        Assert.Equal(2, context.RefreshTokens.Count());

        // Yeni API istegini simule etmek icin takip edilen entity'leri temizle.
        context.ChangeTracker.Clear();

        var replayedRefresh = await Assert.ThrowsAsync<AppException>(() =>
            service.RefreshAsync(
                new RefreshTokenRequest
                {
                    RefreshToken = login.RefreshToken
                },
                CancellationToken.None));

        Assert.Equal(
            "REFRESH_TOKEN_REUSE_DETECTED",
            replayedRefresh.ErrorCode);

        // Veritabaninda aktif refresh token kalmadigini dogrula.
        Assert.Equal(
            0,
            context.RefreshTokens.Count(token => token.RevokedAtUtc == null));

        // Eski entity durumlarinin kullanilmasini engelle.
        context.ChangeTracker.Clear();

        var revokedRefresh = await Assert.ThrowsAsync<AppException>(() =>
            service.RefreshAsync(
                new RefreshTokenRequest
                {
                    RefreshToken = refresh.RefreshToken
                },
                CancellationToken.None));

        Assert.Equal(
            "INVALID_REFRESH_TOKEN",
            revokedRefresh.ErrorCode);

    }

    private const string TestEmail = "race@example.test";
    private const string Password = "a-strong-password";
    private const string NewPassword = "another-strong-password";

    [PostgresFact]
    public async Task Concurrent_refresh_reuse_revokes_the_family()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var login = await SeedLoginAsync(database);
        var results = await RaceAsync(
            () => TryRefreshAsync(database, login.RefreshToken),
            () => TryRefreshAsync(database, login.RefreshToken));
        var responses = new[] { results.First, results.Second };
        Assert.Single(responses, x => x.Response != null);
        Assert.Single(responses, x => x.Error == "REFRESH_TOKEN_REUSE_DETECTED");
        await using var context = database.CreateContext();
        Assert.Equal(2, await context.RefreshTokens.CountAsync());
        Assert.Equal(0, await context.RefreshTokens.CountAsync(x => x.RevokedAtUtc == null));
        Assert.Equal(1, (await context.Users.SingleAsync()).TokenVersion);
    }

    [PostgresFact]
    public async Task Different_token_families_can_refresh_independently()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var first = await SeedLoginAsync(database);
        var second = await LoginAsync(database);
        var results = await RaceAsync(
            () => TryRefreshAsync(database, first.RefreshToken),
            () => TryRefreshAsync(database, second.RefreshToken));
        Assert.NotNull(results.First.Response);
        Assert.NotNull(results.Second.Response);
        await using var context = database.CreateContext();
        var tokens = await context.RefreshTokens.AsNoTracking().ToListAsync();
        Assert.Equal(4, tokens.Count);
        Assert.Equal(2, tokens.Select(x => x.FamilyId).Distinct().Count());
        Assert.All(tokens.GroupBy(x => x.FamilyId), family =>
            Assert.Single(family, x => x.RevokedAtUtc == null));
    }

    [PostgresFact]
    public async Task Logout_with_rotated_token_revokes_only_its_family()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var first = await SeedLoginAsync(database);
        var other = await LoginAsync(database);
        var rotated = await TryRefreshAsync(database, first.RefreshToken);
        Assert.NotNull(rotated.Response);
        await LogoutAsync(database, first.RefreshToken);
        await AssertActiveCountAsync(database, 1);
        var rejected = await TryRefreshAsync(database, rotated.Response!.RefreshToken);
        Assert.Equal("INVALID_REFRESH_TOKEN", rejected.Error);
        Assert.NotNull((await TryRefreshAsync(database, other.RefreshToken)).Response);
    }

    [PostgresFact]
    public async Task Concurrent_refresh_and_logout_leave_no_active_family_token()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var login = await SeedLoginAsync(database);
        var results = await RaceAsync(
            () => TryRefreshAsync(database, login.RefreshToken),
            async () => { await LogoutAsync(database, login.RefreshToken); return true; });
        Assert.True(results.Second);
        Assert.True(results.First.Response != null || results.First.Error == "INVALID_REFRESH_TOKEN");
        await AssertActiveCountAsync(database, 0);
    }

    [PostgresFact]
    public async Task Password_change_revokes_all_refresh_families()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var first = await SeedLoginAsync(database);
        var second = await LoginAsync(database);
        await ChangePasswordAsync(database, first.UserId);
        await AssertActiveCountAsync(database, 0);
        Assert.Equal("INVALID_REFRESH_TOKEN", (await TryRefreshAsync(database, first.RefreshToken)).Error);
        Assert.Equal("INVALID_REFRESH_TOKEN", (await TryRefreshAsync(database, second.RefreshToken)).Error);
        Assert.NotNull(await LoginAsync(database, NewPassword));
    }

    [PostgresFact]
    public async Task Concurrent_refresh_and_password_change_leave_no_active_old_session()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var login = await SeedLoginAsync(database);
        var results = await RaceAsync(
            () => TryRefreshAsync(database, login.RefreshToken),
            async () => { await ChangePasswordAsync(database, login.UserId); return true; });
        Assert.True(results.Second);
        Assert.True(results.First.Response != null || results.First.Error == "INVALID_REFRESH_TOKEN");
        await AssertActiveCountAsync(database, 0);
        await using var context = database.CreateContext();
        Assert.Equal(1, (await context.Users.SingleAsync()).TokenVersion);
    }

    [PostgresFact]
    public async Task Concurrent_reuse_in_two_families_increments_version_twice()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var first = await SeedLoginAsync(database);
        var second = await LoginAsync(database);
        Assert.NotNull((await TryRefreshAsync(database, first.RefreshToken)).Response);
        Assert.NotNull((await TryRefreshAsync(database, second.RefreshToken)).Response);
        var results = await RaceAsync(
            () => TryRefreshAsync(database, first.RefreshToken),
            () => TryRefreshAsync(database, second.RefreshToken));
        Assert.Equal("REFRESH_TOKEN_REUSE_DETECTED", results.First.Error);
        Assert.Equal("REFRESH_TOKEN_REUSE_DETECTED", results.Second.Error);
        await AssertActiveCountAsync(database, 0);
        await using var context = database.CreateContext();
        Assert.Equal(2, (await context.Users.SingleAsync()).TokenVersion);
    }

    [PostgresFact]
    public async Task Jwt_version_claim_matches_current_user_version_after_password_change()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var oldLogin = await SeedLoginAsync(database);
        await ChangePasswordAsync(database, oldLogin.UserId);
        var newLogin = await LoginAsync(database, NewPassword);
        await using var context = database.CreateContext();
        var user = await context.Users.SingleAsync();
        Assert.Equal(1, user.TokenVersion);
        Assert.Equal("0", ReadVersion(oldLogin.AccessToken));
        Assert.Equal(user.TokenVersion.ToString(), ReadVersion(newLogin.AccessToken));
    }

    [PostgresFact]
    public async Task Concurrent_login_and_password_change_leave_no_active_old_session()
    {
        await using var database =
            await PostgresTestDatabase.CreateAsync();

        var initialLogin = await SeedLoginAsync(database);

        var results = await RaceAsync(
            async () =>
            {
                try
                {
                    return await LoginAsync(database, Password);
                }
                catch (AppException exception)
                    when (exception.ErrorCode == "INVALID_CREDENTIALS")
                {
                    return null;
                }
            },
            async () =>
            {
                await ChangePasswordAsync(
                    database, initialLogin.UserId);

                return true;
            });

        Assert.True(results.Second);

        await AssertActiveCountAsync(database, 0);

        if (results.First != null)
        {
            var refresh = await TryRefreshAsync(
                database, results.First.RefreshToken);

            Assert.Equal(
                "INVALID_REFRESH_TOKEN", refresh.Error);
        }
    }

    [PostgresFact]
    public async Task Old_password_cannot_create_session_after_password_change()
    {
        await using var database =
            await PostgresTestDatabase.CreateAsync();

        var initialLogin = await SeedLoginAsync(database);

        await ChangePasswordAsync(
            database, initialLogin.UserId);

        var exception = await Assert.ThrowsAsync<AppException>(
            () => LoginAsync(database, Password));

        Assert.Equal(
            "INVALID_CREDENTIALS", exception.ErrorCode);

        await AssertActiveCountAsync(database, 0);

        var newLogin = await LoginAsync(
            database, NewPassword);

        Assert.NotNull(newLogin);
        Assert.Equal("1", ReadVersion(newLogin.AccessToken));

        await AssertActiveCountAsync(database, 1);
    }

    [PostgresFact]
    public async Task Concurrent_set_password_allows_only_one_success()
    {
        await using var database =
            await PostgresTestDatabase.CreateAsync();

        var initialLogin = await SeedLoginAsync(database);

        await using (var context = database.CreateContext())
        {
            var user = await context.Users.SingleAsync();

            user.PasswordHash = null;

            await context.SaveChangesAsync();
        }

        async Task<string> TrySetPasswordAsync(string password)
        {
            await using var context = database.CreateContext();

            try
            {
                await CreateService(context).SetPasswordAsync(
                    initialLogin.UserId,
                    new SetPasswordRequest
                    {
                        NewPassword = password
                    },
                    CancellationToken.None);

                return "SUCCESS";
            }
            catch (AppException exception)
            {
                return exception.ErrorCode;
            }
        }

        var results = await RaceAsync(
            () => TrySetPasswordAsync(Password),
            () => TrySetPasswordAsync(NewPassword));

        var outcomes = new[]
        {
        results.First,
        results.Second
    };

        Assert.Single(outcomes, x => x == "SUCCESS");

        Assert.Single(
            outcomes, x => x == "PASSWORD_ALREADY_SET");

        var winningPassword = results.First == "SUCCESS"
            ? Password
            : NewPassword;

        Assert.NotNull(
            await LoginAsync(database, winningPassword));
    }

    private const string GoogleSubject = "google-test-subject";

    private sealed class FakeGoogleValidator : IGoogleTokenValidator
    {
        private readonly Func<CancellationToken, Task<GoogleJsonWebSignature.Payload>> _validate;
        public FakeGoogleValidator(Func<CancellationToken, Task<GoogleJsonWebSignature.Payload>>? validate = null)
        {
            _validate = validate ?? (_ => Task.FromResult(new GoogleJsonWebSignature.Payload
            {
                Subject = GoogleSubject,
                Email = TestEmail,
                EmailVerified = true
            }));
        }
        public Task<GoogleJsonWebSignature.Payload> ValidateAsync(string idToken, CancellationToken cancellationToken)
            => _validate(cancellationToken);
    }

    private static async Task<AuthResponse> GoogleLoginAsync(PostgresTestDatabase database)
    {
        await using var context = database.CreateContext();
        return await CreateService(context, new FakeGoogleValidator()).GoogleLoginAsync(
            new GoogleLoginRequest { IdToken = "test-token" }, CancellationToken.None);
    }

    private static async Task PrepareGoogleUserAsync(PostgresTestDatabase database, bool verified, bool linked)
    {
        await using var context = database.CreateContext();
        var user = await context.Users.SingleAsync();
        user.EmailVerified = verified;
        user.GoogleId = linked ? GoogleSubject : null;
        await context.SaveChangesAsync();
    }

    [PostgresFact]
    public async Task Concurrent_google_login_and_password_change_preserve_session_consistency()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var initial = await SeedLoginAsync(database);
        await PrepareGoogleUserAsync(database, true, true);
        var result = await RaceAsync(() => GoogleLoginAsync(database), async () =>
        {
            await ChangePasswordAsync(database, initial.UserId);
            return true;
        });
        await using var context = database.CreateContext();
        var user = await context.Users.SingleAsync();
        Assert.Equal(1, user.TokenVersion);
        var active = await context.RefreshTokens.CountAsync(x => x.RevokedAtUtc == null);
        // Google login is legitimate even after password change, but an old-version
        // response must not leave an active refresh token.
        Assert.Equal(ReadVersion(result.First.AccessToken) == "1" ? 1 : 0, active);
    }

    [PostgresFact]
    public async Task Concurrent_google_takeover_requests_both_resolve_to_same_account()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var initial = await SeedLoginAsync(database);
        await PrepareGoogleUserAsync(database, false, false);
        var result = await RaceAsync(() => GoogleLoginAsync(database), () => GoogleLoginAsync(database));
        Assert.Equal(initial.UserId, result.First.UserId);
        Assert.Equal(initial.UserId, result.Second.UserId);
        await AssertActiveCountAsync(database, 2);
    }

    [PostgresFact]
    public async Task Google_link_rejects_old_password_when_changed_during_validation()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var initial = await SeedLoginAsync(database);
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var validator = new FakeGoogleValidator(async ct =>
        {
            entered.TrySetResult(true);
            await release.Task.WaitAsync(ct);
            return new GoogleJsonWebSignature.Payload
            { Subject = GoogleSubject, Email = TestEmail, EmailVerified = true };
        });
        await using var context = database.CreateContext();
        var linking = CreateService(context, validator).LinkGoogleAccountAsync(initial.UserId,
            new LinkGoogleAccountRequest { IdToken = "test-token", CurrentPassword = Password }, timeout.Token);
        try
        {
            await entered.Task.WaitAsync(timeout.Token);
            await ChangePasswordAsync(database, initial.UserId);
        }
        finally { release.TrySetResult(true); }
        var error = await Assert.ThrowsAsync<AppException>(() => linking);
        Assert.Equal("INVALID_CREDENTIALS", error.ErrorCode);
        await using var check = database.CreateContext();
        Assert.Null((await check.Users.SingleAsync()).GoogleId);
    }

    [PostgresFact]
    public async Task Google_takeover_revokes_old_refresh_tokens()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var initial = await SeedLoginAsync(database);
        await PrepareGoogleUserAsync(database, false, false);
        var login = await GoogleLoginAsync(database);
        Assert.Equal(initial.UserId, login.UserId);
        Assert.False(login.HasPassword);
        await AssertActiveCountAsync(database, 1);
        Assert.Equal("INVALID_REFRESH_TOKEN", (await TryRefreshAsync(database, initial.RefreshToken)).Error);
        Assert.NotNull((await TryRefreshAsync(database, login.RefreshToken)).Response);
    }

    [PostgresFact]
    public async Task Concurrent_google_takeover_increments_token_version_only_once()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await SeedLoginAsync(database);
        await PrepareGoogleUserAsync(database, false, false);
        var result = await RaceAsync(() => GoogleLoginAsync(database), () => GoogleLoginAsync(database));
        await using var context = database.CreateContext();
        var user = await context.Users.SingleAsync();
        Assert.Equal(1, user.TokenVersion);
        Assert.Equal("1", ReadVersion(result.First.AccessToken));
        Assert.Equal("1", ReadVersion(result.Second.AccessToken));
        Assert.True(user.EmailVerified);
        Assert.Null(user.PasswordHash);
        Assert.Equal(GoogleSubject, user.GoogleId);
    }

    [PostgresFact]
    public async Task Google_login_cannot_take_over_verified_unlinked_account()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await SeedLoginAsync(database);
        var error = await Assert.ThrowsAsync<AppException>(() => GoogleLoginAsync(database));
        Assert.Equal("GOOGLE_ACCOUNT_LINK_REQUIRED", error.ErrorCode);
        await using var context = database.CreateContext();
        var user = await context.Users.SingleAsync();
        Assert.Null(user.GoogleId);
        Assert.NotNull(user.PasswordHash);
        Assert.Equal(0, user.TokenVersion);
        await AssertActiveCountAsync(database, 1);
    }

    private static string ReadVersion(string token) =>
        new JwtSecurityTokenHandler().ReadJwtToken(token).Claims
            .Single(x => x.Type == "token_version").Value;

    private static AuthService CreateService(ApplicationDbContext context, IGoogleTokenValidator? googleTokenValidator = null) => new(
        context, new PasswordHasher<User>(), Options.Create(new JwtSettings
        {
            Secret = new string('s', 64),
            Issuer = "DailyChessPuzzleApi.Tests",
            Audience = "DailyChessPuzzleClient.Tests",
            ExpirationMinutes = 60
        }), new ConfigurationBuilder().Build(), new RecordingEmailSender(), googleTokenValidator);

    private static async Task<AuthResponse> SeedLoginAsync(PostgresTestDatabase database)
    {
        await using var context = database.CreateContext();
        var user = new User
        {
            Username = "auth-race",
            FirstName = "Auth",
            LastName = "Race",
            Email = TestEmail,
            EmailVerified = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, Password);
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return await LoginAsync(database);
    }

    private static async Task<AuthResponse> LoginAsync(PostgresTestDatabase database, string password = Password)
    {
        await using var context = database.CreateContext();
        return await CreateService(context).LoginAsync(new LoginRequest
        { Email = TestEmail, Password = password }, CancellationToken.None);
    }

    private static async Task<(AuthResponse? Response, string? Error)> TryRefreshAsync(
        PostgresTestDatabase database, string token)
    {
        await using var context = database.CreateContext();
        try
        {
            return (await CreateService(context).RefreshAsync(new RefreshTokenRequest
            { RefreshToken = token }, CancellationToken.None), null);
        }
        catch (AppException exception) { return (null, exception.ErrorCode); }
    }

    private static async Task LogoutAsync(PostgresTestDatabase database, string token)
    {
        await using var context = database.CreateContext();
        await CreateService(context).LogoutAsync(new RefreshTokenRequest
        { RefreshToken = token }, CancellationToken.None);
    }

    private static async Task ChangePasswordAsync(PostgresTestDatabase database, int userId)
    {
        await using var context = database.CreateContext();
        await CreateService(context).ChangePasswordAsync(userId, new ChangePasswordRequest
        { CurrentPassword = Password, NewPassword = NewPassword }, CancellationToken.None);
    }

    private static async Task AssertActiveCountAsync(PostgresTestDatabase database, int expected)
    {
        await using var context = database.CreateContext();
        Assert.Equal(expected, await context.RefreshTokens.CountAsync(x => x.RevokedAtUtc == null));
    }

    private static async Task<(TFirst First, TSecond Second)> RaceAsync<TFirst, TSecond>(
        Func<Task<TFirst>> first, Func<Task<TSecond>> second)
    {
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var a = Task.Run(async () => { await gate.Task; return await first(); });
        var b = Task.Run(async () => { await gate.Task; return await second(); });
        gate.SetResult(true);
        await Task.WhenAll(a, b).WaitAsync(TimeSpan.FromSeconds(60));
        return (await a, await b);
    }

    private sealed class RecordingEmailSender : IEmailSender
    {
        public string? LastCode { get; private set; }

        public void EnsureConfigured()
        {
        }

        public Task SendVerificationCodeAsync(
            string email,
            string code,
            CancellationToken cancellationToken)
        {
            LastCode = code;
            return Task.CompletedTask;
        }
    }
}
