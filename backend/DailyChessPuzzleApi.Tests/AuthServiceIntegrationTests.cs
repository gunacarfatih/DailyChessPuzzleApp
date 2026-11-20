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
            new RefreshTokenRequest { RefreshToken = login.RefreshToken },
            CancellationToken.None);

        Assert.NotEqual(login.RefreshToken, refresh.RefreshToken);
        Assert.Equal(2, context.RefreshTokens.Count());

        var replayedRefresh = await Assert.ThrowsAsync<AppException>(() =>
            service.RefreshAsync(
                new RefreshTokenRequest { RefreshToken = login.RefreshToken },
                CancellationToken.None));
        Assert.Equal("INVALID_REFRESH_TOKEN", replayedRefresh.ErrorCode);
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
