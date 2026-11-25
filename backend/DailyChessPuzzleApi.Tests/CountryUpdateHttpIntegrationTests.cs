
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using DailyChessPuzzleApi.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace DailyChessPuzzleApi.Tests;

[Collection("Postgres integration")]
public sealed class CountryUpdateHttpIntegrationTests
{
    [PostgresFact]
    public async Task FirstCountryAssignment_ShouldSucceed()
    {
        await using var db =
            await PostgresTestDatabase.CreateAsync();

        using var factory = new TestWebApplicationFactory(db);
        using var client = factory.CreateClient();

        var id = await SeedUserAsync(db);

        using var response =
            await UpdateCountryAsync(client, id, "TR");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var user = await ReadUserAsync(db, id);

        Assert.Equal("TR", user.CountryCode);
        Assert.NotNull(user.CountryChangedAtUtc);
    }

    [PostgresFact]
    public async Task SameCountry_ShouldNotResetCooldown()
    {
        await using var db =
            await PostgresTestDatabase.CreateAsync();

        using var factory = new TestWebApplicationFactory(db);
        using var client = factory.CreateClient();

        var id = await SeedUserAsync(
            db, "TR", DateTime.UtcNow.AddHours(-2));

        var before = await ReadUserAsync(db, id);

        using var response =
            await UpdateCountryAsync(client, id, "TR");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var after = await ReadUserAsync(db, id);

        Assert.Equal("TR", after.CountryCode);
        Assert.Equal(
            before.CountryChangedAtUtc,
            after.CountryChangedAtUtc);

        Assert.Equal(before.UpdatedAtUtc, after.UpdatedAtUtc);
    }

    [PostgresFact]
    public async Task ChangeBefore24Hours_ShouldBeRejected()
    {
        await using var db =
            await PostgresTestDatabase.CreateAsync();

        using var factory = new TestWebApplicationFactory(db);
        using var client = factory.CreateClient();

        var id = await SeedUserAsync(
            db, "TR", DateTime.UtcNow.AddHours(-23));

        var before = await ReadUserAsync(db, id);

        using var response =
            await UpdateCountryAsync(client, id, "DE");

        await AssertErrorAsync(
            response,
            HttpStatusCode.Conflict,
            "COUNTRY_CHANGE_COOLDOWN");

        var after = await ReadUserAsync(db, id);

        Assert.Equal("TR", after.CountryCode);
        Assert.Equal(
            before.CountryChangedAtUtc,
            after.CountryChangedAtUtc);
    }

    [PostgresFact]
    public async Task ChangeAfter24Hours_ShouldSucceed()
    {
        await using var db =
            await PostgresTestDatabase.CreateAsync();

        using var factory = new TestWebApplicationFactory(db);
        using var client = factory.CreateClient();

        var id = await SeedUserAsync(
            db, "TR", DateTime.UtcNow.AddHours(-25));

        var before = await ReadUserAsync(db, id);

        using var response =
            await UpdateCountryAsync(client, id, "DE");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var after = await ReadUserAsync(db, id);

        Assert.Equal("DE", after.CountryCode);
        Assert.NotNull(after.CountryChangedAtUtc);

        Assert.True(
            after.CountryChangedAtUtc >
            before.CountryChangedAtUtc);
    }

    [PostgresFact]
    public async Task LegacyUserWithoutTimestamp_ShouldChangeCountry()
    {
        await using var db =
            await PostgresTestDatabase.CreateAsync();

        using var factory = new TestWebApplicationFactory(db);
        using var client = factory.CreateClient();

        var id = await SeedUserAsync(db, "TR");

        using var response =
            await UpdateCountryAsync(client, id, "DE");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var user = await ReadUserAsync(db, id);

        Assert.Equal("DE", user.CountryCode);
        Assert.NotNull(user.CountryChangedAtUtc);
    }

    [PostgresFact]
    public async Task LowercaseCountry_ShouldBeNormalized()
    {
        await using var db =
            await PostgresTestDatabase.CreateAsync();

        using var factory = new TestWebApplicationFactory(db);
        using var client = factory.CreateClient();

        var id = await SeedUserAsync(db);

        using var response =
            await UpdateCountryAsync(client, id, "de");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var user = await ReadUserAsync(db, id);

        Assert.Equal("DE", user.CountryCode);
    }

    [PostgresFact]
    public async Task InvalidIsoCountry_ShouldBeRejected()
    {
        await using var db =
            await PostgresTestDatabase.CreateAsync();

        using var factory = new TestWebApplicationFactory(db);
        using var client = factory.CreateClient();

        var id = await SeedUserAsync(db, "TR");

        foreach (var code in new[] { "XX", "ZZ", "UK", "12", "TUR" })
        {
            using var response =
                await UpdateCountryAsync(client, id, code);

            Assert.Equal(
                HttpStatusCode.BadRequest,
                response.StatusCode);
        }

        var user = await ReadUserAsync(db, id);

        Assert.Equal("TR", user.CountryCode);
        Assert.Null(user.CountryChangedAtUtc);
    }

    [PostgresFact]
    public async Task EmptyCountry_ShouldBeRejected()
    {
        await using var db =
            await PostgresTestDatabase.CreateAsync();

        using var factory = new TestWebApplicationFactory(db);
        using var client = factory.CreateClient();

        var id = await SeedUserAsync(db, "TR");

        using var response =
            await UpdateCountryAsync(client, id, "");

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);

        var user = await ReadUserAsync(db, id);

        Assert.Equal("TR", user.CountryCode);
        Assert.Null(user.CountryChangedAtUtc);
    }

    [PostgresFact]
    public async Task NullCountry_ShouldBeRejected()
    {
        await using var db =
            await PostgresTestDatabase.CreateAsync();

        using var factory = new TestWebApplicationFactory(db);
        using var client = factory.CreateClient();

        var id = await SeedUserAsync(db, "TR");

        using var response =
            await UpdateCountryAsync(client, id, null);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);

        var user = await ReadUserAsync(db, id);

        Assert.Equal("TR", user.CountryCode);
        Assert.Null(user.CountryChangedAtUtc);
    }

    [PostgresFact]
    public async Task UnauthenticatedRequest_ShouldBeRejected()
    {
        await using var db =
            await PostgresTestDatabase.CreateAsync();

        using var factory = new TestWebApplicationFactory(db);
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(
            HttpMethod.Patch,
            "/api/users/me");

        request.Content = JsonContent.Create(
            new { countryCode = "DE" });

        using var response = await client.SendAsync(request);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    [PostgresFact]
    public async Task ConcurrentDifferentCountries_OnlyOneShouldSucceed()
    {
        await using var db =
            await PostgresTestDatabase.CreateAsync();

        var gate = new CountryUpdateRaceInterceptor();

        using var factory =
            new TestWebApplicationFactory(db, gate);

        using var client = factory.CreateClient();

        var id = await SeedUserAsync(
            db, "TR", DateTime.UtcNow.AddHours(-25));

        var before = await ReadUserAsync(db, id);

        var responses = await Task.WhenAll(
            UpdateCountryAsync(client, id, "DE"),
            UpdateCountryAsync(client, id, "FR"));

        try
        {
            Assert.Equal(2, gate.ArrivedCount);

            Assert.Single(responses.Where(
                x => x.StatusCode == HttpStatusCode.OK));

            var rejected = Assert.Single(responses.Where(
                x => x.StatusCode == HttpStatusCode.Conflict));

            await AssertErrorAsync(
                rejected,
                HttpStatusCode.Conflict,
                "COUNTRY_CHANGE_COOLDOWN");

            var expectedCountry =
                responses[0].StatusCode == HttpStatusCode.OK
                    ? "DE"
                    : "FR";

            var user = await ReadUserAsync(db, id);

            Assert.Equal(expectedCountry, user.CountryCode);
            Assert.NotNull(user.CountryChangedAtUtc);

            Assert.True(
                user.CountryChangedAtUtc >
                before.CountryChangedAtUtc);

            // Üçüncü bir ülkeye geçiş de reddedilmeli.
            using var thirdResponse =
                await UpdateCountryAsync(client, id, "GB");

            await AssertErrorAsync(
                thirdResponse,
                HttpStatusCode.Conflict,
                "COUNTRY_CHANGE_COOLDOWN");

            var after = await ReadUserAsync(db, id);

            Assert.Equal(user.CountryCode, after.CountryCode);

            Assert.Equal(
                user.CountryChangedAtUtc,
                after.CountryChangedAtUtc);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [PostgresFact]
    public async Task ConcurrentSameCountry_BothShouldSucceed()
    {
        await using var db =
            await PostgresTestDatabase.CreateAsync();

        var gate = new CountryUpdateRaceInterceptor();

        using var factory =
            new TestWebApplicationFactory(db, gate);

        using var client = factory.CreateClient();

        var id = await SeedUserAsync(
            db, "TR", DateTime.UtcNow.AddHours(-25));

        var responses = await Task.WhenAll(
            UpdateCountryAsync(client, id, "DE"),
            UpdateCountryAsync(client, id, "DE"));

        try
        {
            Assert.Equal(2, gate.ArrivedCount);

            Assert.All(responses, response =>
                Assert.Equal(
                    HttpStatusCode.OK,
                    response.StatusCode));

            var changedCount = 0;
            var timestamps = new List<DateTime>();

            foreach (var response in responses)
            {
                using var json = JsonDocument.Parse(
                    await response.Content.ReadAsStringAsync());

                var data = json.RootElement.GetProperty("data");

                Assert.Equal(
                    "DE",
                    data.GetProperty("countryCode").GetString());

                if (data.GetProperty("countryChanged").GetBoolean())
                {
                    changedCount++;
                }

                timestamps.Add(
                    data.GetProperty("countryChangedAtUtc")
                        .GetDateTime());
            }

            Assert.Equal(1, changedCount);

            Assert.True(
                Math.Abs(
                    (timestamps[0] - timestamps[1]).Ticks) < 10);

            var user = await ReadUserAsync(db, id);

            Assert.Equal("DE", user.CountryCode);
            Assert.NotNull(user.CountryChangedAtUtc);

            Assert.All(timestamps, timestamp =>
                Assert.True(
                    Math.Abs(
                        (timestamp -
                         user.CountryChangedAtUtc!.Value).Ticks) < 10));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    private static async Task<int> SeedUserAsync(
        PostgresTestDatabase db,
        string? countryCode = null,
        DateTime? countryChangedAtUtc = null)
    {
        await using var context = db.CreateContext();

        var user = new User
        {
            Username = "country-test",
            FirstName = "Country",
            LastName = "Test",
            Email = "country-test@example.com",
            EmailVerified = true,
            CountryCode = countryCode,
            CountryChangedAtUtc = countryChangedAtUtc,
            TokenVersion = 0,
            CreatedAtUtc = DateTime.UtcNow
        };

        context.Users.Add(user);

        await context.SaveChangesAsync();

        return user.Id;
    }

    private static async Task<User> ReadUserAsync(
        PostgresTestDatabase db,
        int userId)
    {
        await using var context = db.CreateContext();

        return await context.Users
            .AsNoTracking()
            .SingleAsync(x => x.Id == userId);
    }

    private static async Task<HttpResponseMessage>
        UpdateCountryAsync(
            HttpClient client,
            int userId,
            string? countryCode)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Patch,
            "/api/users/me");

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                CreateAccessToken(userId));

        request.Content = JsonContent.Create(
            new { countryCode });

        return await client.SendAsync(request);
    }

    private static async Task AssertErrorAsync(
        HttpResponseMessage response,
        HttpStatusCode status,
        string errorCode)
    {
        Assert.Equal(status, response.StatusCode);

        using var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        Assert.False(
            json.RootElement.GetProperty("success").GetBoolean());

        Assert.Equal(
            errorCode,
            json.RootElement.GetProperty("errorCode").GetString());
    }

    private static string CreateAccessToken(int userId)
    {
        var claims = new[]
        {
            new Claim(
                JwtRegisteredClaimNames.Sub,
                userId.ToString()),

            new Claim(
                ClaimTypes.NameIdentifier,
                userId.ToString()),

            new Claim("token_version", "0")
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
