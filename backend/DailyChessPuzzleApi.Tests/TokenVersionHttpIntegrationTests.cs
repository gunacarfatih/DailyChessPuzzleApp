using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;

using DailyChessPuzzleApi.DTOs.Auth;
using DailyChessPuzzleApi.Models;
using DailyChessPuzzleApi.Services.Interfaces;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

using Xunit;

namespace DailyChessPuzzleApi.Tests;

[Collection("Postgres integration")]
public sealed class TokenVersionHttpIntegrationTests
{
    private const string Email =
        "http-auth@example.test";

    private const string Password =
        "a-strong-password";

    private const string NewPassword =
        "another-strong-password";

    [PostgresFact]
    public async Task Valid_access_token_can_access_protected_endpoint()
    {
        await using var database =
            await PostgresTestDatabase.CreateAsync();

        using var factory =
            new TestWebApplicationFactory(database);

        using var client = factory.CreateClient();

        var login = await SeedLoginAsync(
            database, factory);

        using var response = await SendChangePasswordAsync(
            client,
            login.AccessToken,
            Password,
            NewPassword);

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
    }

    [PostgresFact]
    public async Task Old_access_token_is_rejected_after_password_change()
    {
        await using var database =
            await PostgresTestDatabase.CreateAsync();

        using var factory =
            new TestWebApplicationFactory(database);

        using var client = factory.CreateClient();

        var login = await SeedLoginAsync(
            database, factory);

        using var changed = await SendChangePasswordAsync(
            client,
            login.AccessToken,
            Password,
            NewPassword);

        Assert.Equal(
            HttpStatusCode.OK,
            changed.StatusCode);

        using var rejected = await SendChangePasswordAsync(
            client,
            login.AccessToken,
            NewPassword,
            Password);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            rejected.StatusCode);
    }

    [PostgresFact]
    public async Task New_access_token_is_accepted_after_password_change()
    {
        await using var database =
            await PostgresTestDatabase.CreateAsync();

        using var factory =
            new TestWebApplicationFactory(database);

        using var client = factory.CreateClient();

        var oldLogin = await SeedLoginAsync(
            database, factory);

        using var changed = await SendChangePasswordAsync(
            client,
            oldLogin.AccessToken,
            Password,
            NewPassword);

        Assert.Equal(
            HttpStatusCode.OK,
            changed.StatusCode);

        var newLogin = await LoginAsync(
            factory, NewPassword);

        using var accepted = await SendChangePasswordAsync(
            client,
            newLogin.AccessToken,
            NewPassword,
            Password);

        Assert.Equal(
            HttpStatusCode.OK,
            accepted.StatusCode);
    }

    [PostgresFact]
    public async Task Refresh_token_reuse_invalidates_previous_access_tokens()
    {
        await using var database =
            await PostgresTestDatabase.CreateAsync();

        using var factory =
            new TestWebApplicationFactory(database);

        using var client = factory.CreateClient();

        var login = await SeedLoginAsync(
            database, factory);

        using var rotated = await client.PostAsJsonAsync(
            "/api/auth/refresh",
            new
            {
                refreshToken = login.RefreshToken
            });

        Assert.Equal(
            HttpStatusCode.OK,
            rotated.StatusCode);

        using var replay = await client.PostAsJsonAsync(
            "/api/auth/refresh",
            new
            {
                refreshToken = login.RefreshToken
            });

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            replay.StatusCode);

        using var rejected = await SendChangePasswordAsync(
            client,
            login.AccessToken,
            Password,
            NewPassword);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            rejected.StatusCode);
    }

    [PostgresFact]
    public async Task Access_token_for_nonexistent_user_is_rejected()
    {
        await using var database =
            await PostgresTestDatabase.CreateAsync();

        using var factory =
            new TestWebApplicationFactory(database);

        using var client = factory.CreateClient();

        var token = CreateAccessToken(
            int.MaxValue,
            tokenVersion: 0);

        using var response = await SendChangePasswordAsync(
            client,
            token,
            Password,
            NewPassword);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    private static async Task<AuthResponse> SeedLoginAsync(
        PostgresTestDatabase database,
        TestWebApplicationFactory factory)
    {
        await using var context =
            database.CreateContext();

        var user = new User
        {
            Username = "http-auth-test",
            FirstName = "Http",
            LastName = "Test",
            Email = Email,
            EmailVerified = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        user.PasswordHash =
            new PasswordHasher<User>()
                .HashPassword(user, Password);

        context.Users.Add(user);

        await context.SaveChangesAsync();

        return await LoginAsync(
            factory, Password);
    }

    private static async Task<AuthResponse> LoginAsync(
        TestWebApplicationFactory factory,
        string password)
    {
        await using var scope =
            factory.Services.CreateAsyncScope();

        var service = scope.ServiceProvider
            .GetRequiredService<IAuthService>();

        return await service.LoginAsync(
            new LoginRequest
            {
                Email = Email,
                Password = password
            },
            CancellationToken.None);
    }

    private static async Task<HttpResponseMessage>
        SendChangePasswordAsync(
            HttpClient client,
            string accessToken,
            string currentPassword,
            string newPassword)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/auth/change-password");

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                accessToken);

        request.Content = JsonContent.Create(
            new
            {
                currentPassword,
                newPassword
            });

        return await client.SendAsync(request);
    }

    private static string CreateAccessToken(
        int userId,
        int tokenVersion)
    {
        var claims = new[]
        {
            new Claim(
                JwtRegisteredClaimNames.Sub,
                userId.ToString()),

            new Claim(
                ClaimTypes.NameIdentifier,
                userId.ToString()),

            new Claim(
                "token_version",
                tokenVersion.ToString())
        };

        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(
                TestWebApplicationFactory.JwtSecret));

        var credentials = new SigningCredentials(
            key,
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: TestWebApplicationFactory.JwtIssuer,
            audience: TestWebApplicationFactory.JwtAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(60),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler()
            .WriteToken(token);
    }
}