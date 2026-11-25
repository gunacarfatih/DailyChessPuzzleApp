using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using DailyChessPuzzleApi.DTOs.Auth;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace DailyChessPuzzleApi.Tests;

[Collection("Postgres integration")]
public sealed class RegistrationHttpIntegrationTests
{
    private const string Password =
        "a-strong-password";

    // --------------------------------------------------
    // NORMAL REGISTRATION
    // --------------------------------------------------

    [PostgresFact]
    public async Task Valid_registration_returns_created()
    {
        await using var database =
            await PostgresTestDatabase.CreateAsync();

        using var factory =
            new TestWebApplicationFactory(database);

        using var client = factory.CreateClient();

        using var response = await RegisterAsync(
            client,
            "ChessPlayer",
            "player@example.test");

        await AssertSuccessAsync(response);

        await using var context =
            database.CreateContext();

        var user = await context.Users.SingleAsync();

        Assert.Equal("ChessPlayer", user.Username);

        Assert.Equal(
            "chessplayer",
            user.NormalizedUsername);

        Assert.Equal(
            "player@example.test",
            user.Email);

        Assert.False(user.EmailVerified);

        Assert.NotNull(user.PasswordHash);

        Assert.NotNull(
            user.EmailVerificationCodeHash);

        Assert.NotNull(
            user.EmailVerificationCodeExpiresAtUtc);

        Assert.Equal(0, user.TokenVersion);

        Assert.Empty(
            await context.RefreshTokens.ToListAsync());
    }

    // --------------------------------------------------
    // SEQUENTIAL REGISTRATION CONFLICTS
    // --------------------------------------------------

    [PostgresFact]
    public async Task Duplicate_email_returns_conflict()
    {
        await using var database =
            await PostgresTestDatabase.CreateAsync();

        using var factory =
            new TestWebApplicationFactory(database);

        using var client = factory.CreateClient();

        using var first = await RegisterAsync(
            client,
            "first-player",
            "same@example.test");

        await AssertSuccessAsync(first);

        using var second = await RegisterAsync(
            client,
            "second-player",
            "same@example.test");

        await AssertFailureAsync(
            second,
            HttpStatusCode.Conflict,
            "EMAIL_ALREADY_EXISTS");

        await using var context =
            database.CreateContext();

        Assert.Equal(
            1,
            await context.Users.CountAsync());
    }

    [PostgresFact]
    public async Task Duplicate_username_returns_conflict()
    {
        await using var database =
            await PostgresTestDatabase.CreateAsync();

        using var factory =
            new TestWebApplicationFactory(database);

        using var client = factory.CreateClient();

        using var first = await RegisterAsync(
            client,
            "same-player",
            "first@example.test");

        await AssertSuccessAsync(first);

        using var second = await RegisterAsync(
            client,
            "same-player",
            "second@example.test");

        await AssertFailureAsync(
            second,
            HttpStatusCode.Conflict,
            "USERNAME_ALREADY_EXISTS");

        await using var context =
            database.CreateContext();

        Assert.Equal(
            1,
            await context.Users.CountAsync());
    }

    [PostgresFact]
    public async Task Username_with_different_casing_returns_conflict()
    {
        await using var database =
            await PostgresTestDatabase.CreateAsync();

        using var factory =
            new TestWebApplicationFactory(database);

        using var client = factory.CreateClient();

        using var first = await RegisterAsync(
            client,
            "ChessPlayer",
            "first@example.test");

        await AssertSuccessAsync(first);

        using var second = await RegisterAsync(
            client,
            "chessplayer",
            "second@example.test");

        await AssertFailureAsync(
            second,
            HttpStatusCode.Conflict,
            "USERNAME_ALREADY_EXISTS");

        await using var context =
            database.CreateContext();

        var user = await context.Users.SingleAsync();

        Assert.Equal("ChessPlayer", user.Username);

        Assert.Equal(
            "chessplayer",
            user.NormalizedUsername);
    }

    // --------------------------------------------------
    // CONCURRENT REGISTRATION CONFLICTS
    // --------------------------------------------------

    [PostgresFact]
    public async Task Concurrent_registration_with_same_email_returns_created_and_conflict()
    {
        await AssertConcurrentConflictAsync(
            firstUsername: "first-player",
            firstEmail: "same@example.test",
            secondUsername: "second-player",
            secondEmail: "same@example.test",
            expectedErrorCode: "EMAIL_ALREADY_EXISTS");
    }

    [PostgresFact]
    public async Task Concurrent_registration_with_same_username_returns_created_and_conflict()
    {
        await AssertConcurrentConflictAsync(
            firstUsername: "same-player",
            firstEmail: "first@example.test",
            secondUsername: "same-player",
            secondEmail: "second@example.test",
            expectedErrorCode: "USERNAME_ALREADY_EXISTS");
    }

    [PostgresFact]
    public async Task Concurrent_registration_with_different_username_casing_returns_created_and_conflict()
    {
        await AssertConcurrentConflictAsync(
            firstUsername: "ChessPlayer",
            firstEmail: "first@example.test",
            secondUsername: "chessplayer",
            secondEmail: "second@example.test",
            expectedErrorCode: "USERNAME_ALREADY_EXISTS");
    }

    [PostgresFact]
    public async Task Concurrent_registration_of_distinct_users_returns_two_created_responses()
    {
        await using var database =
            await PostgresTestDatabase.CreateAsync();

        var gate = new UserWriteRaceInterceptor();

        using var factory =
            new TestWebApplicationFactory(
                database,
                gate);

        using var client = factory.CreateClient();

        var responses = await Task.WhenAll(
            RegisterAsync(
                client,
                "first-player",
                "first@example.test"),

            RegisterAsync(
                client,
                "second-player",
                "second@example.test"));

        using var first = responses[0];
        using var second = responses[1];

        Assert.Equal(2, gate.ArrivedCount);

        await AssertSuccessAsync(first);
        await AssertSuccessAsync(second);

        await using var context =
            database.CreateContext();

        var users = await context.Users
            .OrderBy(user => user.Username)
            .ToListAsync();

        Assert.Equal(2, users.Count);

        Assert.Equal(
            "first-player",
            users[0].Username);

        Assert.Equal(
            "first@example.test",
            users[0].Email);

        Assert.Equal(
            "second-player",
            users[1].Username);

        Assert.Equal(
            "second@example.test",
            users[1].Email);

        Assert.All(
            users,
            user => Assert.False(user.EmailVerified));

        Assert.Empty(
            await context.RefreshTokens.ToListAsync());
    }

    // --------------------------------------------------
    // USERNAME VALIDATION
    // --------------------------------------------------

    [PostgresFact]
    public async Task Username_shorter_than_minimum_after_trim_returns_bad_request()
    {
        await using var database =
            await PostgresTestDatabase.CreateAsync();

        using var factory =
            new TestWebApplicationFactory(database);

        using var client = factory.CreateClient();

        using var response = await RegisterAsync(
            client,
            "  ab  ",
            "player@example.test");

        await AssertFailureAsync(
            response,
            HttpStatusCode.BadRequest,
            "INVALID_USERNAME");

        await using var context =
            database.CreateContext();

        Assert.Equal(
            0,
            await context.Users.CountAsync());
    }

    [PostgresFact]
    public async Task Username_duplicate_after_trim_returns_conflict()
    {
        await using var database =
            await PostgresTestDatabase.CreateAsync();

        using var factory =
            new TestWebApplicationFactory(database);

        using var client = factory.CreateClient();

        using var first = await RegisterAsync(
            client,
            "  ChessPlayer  ",
            "first@example.test");

        await AssertSuccessAsync(first);

        using var second = await RegisterAsync(
            client,
            " chessplayer ",
            "second@example.test");

        await AssertFailureAsync(
            second,
            HttpStatusCode.Conflict,
            "USERNAME_ALREADY_EXISTS");

        await using var context =
            database.CreateContext();

        var user = await context.Users.SingleAsync();

        Assert.Equal(
            "ChessPlayer",
            user.Username);

        Assert.Equal(
            "chessplayer",
            user.NormalizedUsername);
    }

    // --------------------------------------------------
    // HELPERS
    // --------------------------------------------------

    private static Task<HttpResponseMessage> RegisterAsync(
        HttpClient client,
        string username,
        string email)
    {
        return client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest
            {
                Username = username,
                FirstName = "Registration",
                LastName = "Test",
                Email = email,
                Password = Password,
                CountryCode = "TR"
            });
    }

    private static async Task AssertSuccessAsync(
        HttpResponseMessage response)
    {
        var content =
            await response.Content.ReadAsStringAsync();

        Assert.True(
            response.StatusCode == HttpStatusCode.Created,
            $"Expected HTTP 201, received " +
            $"{(int)response.StatusCode}. Body: {content}");

        using var document =
            JsonDocument.Parse(content);

        var root = document.RootElement;

        Assert.True(
            root.GetProperty("success").GetBoolean());
    }

    private static async Task AssertFailureAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedErrorCode)
    {
        var content =
            await response.Content.ReadAsStringAsync();

        Assert.True(
            response.StatusCode == expectedStatus,
            $"Expected HTTP {(int)expectedStatus}, " +
            $"received {(int)response.StatusCode}. " +
            $"Body: {content}");

        using var document =
            JsonDocument.Parse(content);

        var root = document.RootElement;

        Assert.False(
            root.GetProperty("success").GetBoolean());

        Assert.Equal(
            (int)expectedStatus,
            root.GetProperty("status").GetInt32());

        Assert.Equal(
            expectedErrorCode,
            root.GetProperty("errorCode").GetString());
    }

    private static async Task AssertConcurrentConflictAsync(
        string firstUsername,
        string firstEmail,
        string secondUsername,
        string secondEmail,
        string expectedErrorCode)
    {
        await using var database =
            await PostgresTestDatabase.CreateAsync();

        var gate = new UserWriteRaceInterceptor();

        using var factory =
            new TestWebApplicationFactory(
                database,
                gate);

        using var client = factory.CreateClient();

        var responses = await Task.WhenAll(
            RegisterAsync(
                client,
                firstUsername,
                firstEmail),

            RegisterAsync(
                client,
                secondUsername,
                secondEmail));

        using var first = responses[0];
        using var second = responses[1];

        Assert.Equal(2, gate.ArrivedCount);

        var successfulResponse = Assert.Single(
            responses.Where(response =>
                response.StatusCode ==
                HttpStatusCode.Created));

        var conflictResponse = Assert.Single(
            responses.Where(response =>
                response.StatusCode ==
                HttpStatusCode.Conflict));

        await AssertSuccessAsync(
            successfulResponse);

        await AssertFailureAsync(
            conflictResponse,
            HttpStatusCode.Conflict,
            expectedErrorCode);

        await using var context =
            database.CreateContext();

        var user = await context.Users.SingleAsync();

        if (ReferenceEquals(
                successfulResponse,
                first))
        {
            Assert.Equal(
                firstUsername,
                user.Username);

            Assert.Equal(
                firstEmail,
                user.Email);
        }
        else
        {
            Assert.Equal(
                secondUsername,
                user.Username);

            Assert.Equal(
                secondEmail,
                user.Email);
        }

        Assert.False(user.EmailVerified);

        Assert.NotNull(user.PasswordHash);

        Assert.Empty(
            await context.RefreshTokens.ToListAsync());
    }
}