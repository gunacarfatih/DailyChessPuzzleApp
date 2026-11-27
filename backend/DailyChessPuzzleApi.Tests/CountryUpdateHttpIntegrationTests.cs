using DailyChessPuzzleApi.Data;
using DailyChessPuzzleApi.DTOs;
using DailyChessPuzzleApi.Services;
using DailyChessPuzzleApi.Services.Interfaces;
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
using Microsoft.Extensions.Logging.Abstractions;
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

            Assert.Single(
                responses,
                x => x.StatusCode == HttpStatusCode.OK);

            var rejected = Assert.Single(
                responses,
                x => x.StatusCode == HttpStatusCode.Conflict);

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

    [PostgresFact]
    public async Task Concurrent_country_change_and_daily_start_preserve_session_country()
    {
        await using var db =
            await PostgresTestDatabase.CreateAsync();

        await db.SeedDailyPuzzleAsync(
            PuzzleAudience.Registered);

        var userId = await SeedUserAsync(
            db,
            "TR",
            DateTime.UtcNow.AddHours(-25));

        var gate = new CountryPuzzleRaceInterceptor(
            CountryPuzzleRaceOperation.DailyStart);

        using var factory =
            new TestWebApplicationFactory(db, gate);

        using var client = factory.CreateClient();

        await using var puzzleContext =
            db.CreateContext(gate);

        var puzzleService =
            CreateCountryPuzzleService(puzzleContext);

        var countryTask = UpdateCountryAsync(
            client,
            userId,
            "DE");

        var puzzleTask = puzzleService.StartSessionAsync(
            userId,
            null);

        await Task.WhenAll(
            countryTask,
            puzzleTask)
            .WaitAsync(TimeSpan.FromSeconds(30));

        using var countryResponse = await countryTask;

        var sessionResponse = await puzzleTask;

        Assert.Equal(2, gate.ArrivedCount);

        Assert.Equal(
            HttpStatusCode.OK,
            countryResponse.StatusCode);

        Assert.NotNull(sessionResponse.SessionId);

        await using var verificationContext =
            db.CreateContext();

        var user = await verificationContext.Users
            .AsNoTracking()
            .SingleAsync(x => x.Id == userId);

        Assert.Equal("DE", user.CountryCode);

        var session = Assert.Single(
            await verificationContext.PuzzleGameSessions
                .AsNoTracking()
                .Where(x => x.UserId == userId)
                .ToListAsync());

        Assert.Contains(
            session.CountryCode,
            new[] { "TR", "DE" });

        Assert.Equal(
            sessionResponse.SessionId!.Value,
            session.Id);

        var originalCountry = session.CountryCode;

        // Oturuma tekrar girildiğinde başlangıç ülkesi değişmemeli.
        await using (var resumeContext = db.CreateContext())
        {
            var service =
                CreateCountryPuzzleService(resumeContext);

            var resumed = await service.StartSessionAsync(
                userId,
                null);

            Assert.Equal(session.Id, resumed.SessionId);
        }

        // Puzzle tamamlandığında puan başlangıç ülkesine yazılmalı.
        await using (var completionContext = db.CreateContext())
        {
            var service =
                CreateCountryPuzzleService(completionContext);

            var completed = await service.SubmitMoveAsync(
                new PuzzleMoveRequest
                {
                    SessionId = session.Id,
                    Uci = "e7e5",
                    ExpectedMoveIndex = 1
                },
                userId);

            Assert.True(completed.IsCompleted);
            Assert.NotNull(completed.Score);
        }

        var completedSession = await verificationContext
            .PuzzleGameSessions
            .AsNoTracking()
            .SingleAsync(x => x.Id == session.Id);

        Assert.True(completedSession.IsCompleted);

        Assert.Equal(
            originalCountry,
            completedSession.CountryCode);

        var attempt = await verificationContext.PuzzleAttempts
            .AsNoTracking()
            .SingleAsync();

        Assert.Equal(originalCountry, attempt.CountryCode);

        var deliveries = await verificationContext
            .LeaderboardCacheOutboxDeliveries
            .AsNoTracking()
            .ToListAsync();

        Assert.Equal(8, deliveries.Count);

        Assert.Equal(
            4,
            deliveries.Count(x => x.CountryCode == null));

        Assert.Equal(
            4,
            deliveries.Count(
                x => x.CountryCode == originalCountry));

        Assert.All(deliveries, delivery =>
        {
            Assert.Equal(attempt.Score, delivery.TotalScore);
            Assert.Equal(1, delivery.SolvedCount);
        });
    }

    [PostgresFact]
    public async Task Concurrent_country_change_and_daily_completion_preserve_original_country()
    {
        await using var db =
            await PostgresTestDatabase.CreateAsync();

        await db.SeedDailyPuzzleAsync(
            PuzzleAudience.Registered);

        var userId = await SeedUserAsync(
            db,
            "TR",
            DateTime.UtcNow.AddHours(-25));

        int sessionId;

        await using (var startContext = db.CreateContext())
        {
            var service =
                CreateCountryPuzzleService(startContext);

            var startedSession = await service.StartSessionAsync(
                userId,
                null);

            Assert.NotNull(startedSession.SessionId);

            sessionId = startedSession.SessionId!.Value;
        }

        var gate = new CountryPuzzleRaceInterceptor(
            CountryPuzzleRaceOperation.DailyCompletion);

        using var factory =
            new TestWebApplicationFactory(db, gate);

        using var client = factory.CreateClient();

        await using var puzzleContext =
            db.CreateContext(gate);

        var puzzleService =
            CreateCountryPuzzleService(puzzleContext);

        var countryTask = UpdateCountryAsync(
            client,
            userId,
            "DE");

        var completionTask = puzzleService.SubmitMoveAsync(
            new PuzzleMoveRequest
            {
                SessionId = sessionId,
                Uci = "e7e5",
                ExpectedMoveIndex = 1
            },
            userId);

        await Task.WhenAll(
            countryTask,
            completionTask)
            .WaitAsync(TimeSpan.FromSeconds(30));

        using var countryResponse = await countryTask;

        var completed = await completionTask;

        Assert.Equal(2, gate.ArrivedCount);

        Assert.Equal(
            HttpStatusCode.OK,
            countryResponse.StatusCode);

        Assert.True(completed.IsCompleted);
        Assert.NotNull(completed.Score);

        await using var verificationContext =
            db.CreateContext();

        var user = await verificationContext.Users
            .AsNoTracking()
            .SingleAsync(x => x.Id == userId);

        Assert.Equal("DE", user.CountryCode);

        var session = await verificationContext
            .PuzzleGameSessions
            .AsNoTracking()
            .SingleAsync(x => x.Id == sessionId);

        Assert.True(session.IsCompleted);
        Assert.Equal("TR", session.CountryCode);

        var attempt = await verificationContext.PuzzleAttempts
            .AsNoTracking()
            .SingleAsync();

        Assert.Equal(userId, attempt.UserId);
        Assert.Equal("TR", attempt.CountryCode);
        Assert.Equal(completed.Score!.Value, attempt.Score);

        Assert.Equal(
            1,
            await verificationContext
                .LeaderboardCacheOutboxes.CountAsync());

        var deliveries = await verificationContext
            .LeaderboardCacheOutboxDeliveries
            .AsNoTracking()
            .ToListAsync();

        Assert.Equal(8, deliveries.Count);

        Assert.Equal(
            4,
            deliveries.Count(x => x.CountryCode == null));

        Assert.Equal(
            4,
            deliveries.Count(x => x.CountryCode == "TR"));

        Assert.DoesNotContain(
            deliveries,
            x => x.CountryCode == "DE");

        Assert.All(deliveries, delivery =>
        {
            Assert.Equal(attempt.Score, delivery.TotalScore);
            Assert.Equal(1, delivery.SolvedCount);
        });
    }

    [PostgresFact]
    public async Task Daily_replay_after_country_change_does_not_create_second_score()
    {
        await using var db =
            await PostgresTestDatabase.CreateAsync();

        await db.SeedDailyPuzzleAsync(
            PuzzleAudience.Registered);

        var userId = await SeedUserAsync(
            db,
            "TR",
            DateTime.UtcNow.AddHours(-25));

        int firstSessionId;
        decimal originalScore;

        await using (var context = db.CreateContext())
        {
            var service =
                CreateCountryPuzzleService(context);

            var session = await service.StartSessionAsync(
                userId,
                null);

            Assert.NotNull(session.SessionId);

            firstSessionId = session.SessionId!.Value;

            var result = await service.SubmitMoveAsync(
                new PuzzleMoveRequest
                {
                    SessionId = firstSessionId,
                    Uci = "e7e5",
                    ExpectedMoveIndex = 1
                },
                userId);

            Assert.True(result.IsCompleted);
            Assert.NotNull(result.Score);

            originalScore = result.Score!.Value;
        }

        using var factory =
            new TestWebApplicationFactory(db);

        using var client = factory.CreateClient();

        using var countryResponse =
            await UpdateCountryAsync(
                client,
                userId,
                "DE");

        Assert.Equal(
            HttpStatusCode.OK,
            countryResponse.StatusCode);

        int secondSessionId;

        await using (var context = db.CreateContext())
        {
            var service =
                CreateCountryPuzzleService(context);

            var session = await service.StartSessionAsync(
                userId,
                null);

            Assert.NotNull(session.SessionId);

            secondSessionId = session.SessionId!.Value;

            Assert.NotEqual(
                firstSessionId,
                secondSessionId);

            var result = await service.SubmitMoveAsync(
                new PuzzleMoveRequest
                {
                    SessionId = secondSessionId,
                    Uci = "e7e5",
                    ExpectedMoveIndex = 1
                },
                userId);

            Assert.True(result.IsCompleted);
            Assert.NotNull(result.Score);
        }

        await using var verificationContext =
            db.CreateContext();

        var user = await verificationContext.Users
            .AsNoTracking()
            .SingleAsync(x => x.Id == userId);

        Assert.Equal("DE", user.CountryCode);

        var sessions = await verificationContext
            .PuzzleGameSessions
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .ToListAsync();

        Assert.Equal(2, sessions.Count);

        var firstSession = Assert.Single(
            sessions,
            x => x.Id == firstSessionId);

        var secondSession = Assert.Single(
            sessions,
            x => x.Id == secondSessionId);

        Assert.Equal("TR", firstSession.CountryCode);
        Assert.Equal("DE", secondSession.CountryCode);

        Assert.All(
            sessions,
            session => Assert.True(session.IsCompleted));

        var attempt = Assert.Single(
            await verificationContext.PuzzleAttempts
                .AsNoTracking()
                .Where(x => x.UserId == userId)
                .ToListAsync());

        Assert.Equal("TR", attempt.CountryCode);
        Assert.Equal(originalScore, attempt.Score);

        Assert.Equal(
            1,
            await verificationContext
                .LeaderboardCacheOutboxes.CountAsync());

        var deliveries = await verificationContext
            .LeaderboardCacheOutboxDeliveries
            .AsNoTracking()
            .ToListAsync();

        Assert.Equal(8, deliveries.Count);

        Assert.Equal(
            4,
            deliveries.Count(x => x.CountryCode == null));

        Assert.Equal(
            4,
            deliveries.Count(x => x.CountryCode == "TR"));

        Assert.DoesNotContain(
            deliveries,
            x => x.CountryCode == "DE");

        Assert.All(deliveries, delivery =>
        {
            Assert.Equal(originalScore, delivery.TotalScore);
            Assert.Equal(1, delivery.SolvedCount);
        });
    }

    [PostgresFact]
    public async Task Concurrent_country_change_and_bonus_start_preserve_assignment_country()
    {
        await using var db =
            await PostgresTestDatabase.CreateAsync();

        var userId = await SeedUserAsync(
            db,
            "TR",
            DateTime.UtcNow.AddHours(-25));

        await PrepareCountryBonusAsync(db, userId);

        var gate = new CountryPuzzleRaceInterceptor(
            CountryPuzzleRaceOperation.BonusStart);

        using var factory =
            new TestWebApplicationFactory(db, gate);

        using var client = factory.CreateClient();

        await using var puzzleContext =
            db.CreateContext(gate);

        var service =
            CreateCountryPuzzleService(puzzleContext);

        var countryTask = UpdateCountryAsync(
            client,
            userId,
            "DE");

        var bonusTask = service.StartBonusAsync(userId);

        await Task.WhenAll(
            countryTask,
            bonusTask)
            .WaitAsync(TimeSpan.FromSeconds(30));

        using var countryResponse = await countryTask;

        var bonusResponse = await bonusTask;

        Assert.Equal(2, gate.ArrivedCount);

        Assert.Equal(
            HttpStatusCode.OK,
            countryResponse.StatusCode);

        Assert.True(bonusResponse.IsEligible);
        Assert.NotNull(bonusResponse.StartedAtUtc);

        await using var verificationContext =
            db.CreateContext();

        var user = await verificationContext.Users
            .AsNoTracking()
            .SingleAsync(x => x.Id == userId);

        Assert.Equal("DE", user.CountryCode);

        var assignment = await verificationContext
            .BonusPuzzleAssignments
            .AsNoTracking()
            .SingleAsync(x => x.UserId == userId);

        Assert.Contains(
            assignment.CountryCode,
            new[] { "TR", "DE" });

        Assert.NotNull(assignment.StartedAtUtc);
        Assert.False(assignment.IsCompleted);

        var originalCountry = assignment.CountryCode;
        var originalStartedAt = assignment.StartedAtUtc;

        // Bonus yeniden başlatıldığında ülke ve süre değişmemeli.
        await using (var resumeContext = db.CreateContext())
        {
            var resumeService =
                CreateCountryPuzzleService(resumeContext);

            var resumed =
                await resumeService.StartBonusAsync(userId);

            Assert.True(resumed.IsEligible);
        }

        var resumedAssignment = await verificationContext
            .BonusPuzzleAssignments
            .AsNoTracking()
            .SingleAsync(x => x.UserId == userId);

        Assert.Equal(
            originalCountry,
            resumedAssignment.CountryCode);

        Assert.Equal(
            originalStartedAt,
            resumedAssignment.StartedAtUtc);

        // Bonus çözülür.
        await using (var moveContext = db.CreateContext())
        {
            var moveService =
                CreateCountryPuzzleService(moveContext);

            var solved = await moveService.SubmitBonusMoveAsync(
                new PuzzleMoveRequest
                {
                    Uci = "e7e5",
                    ExpectedMoveIndex = 1
                },
                userId);

            Assert.True(solved.IsSolved);
        }

        // Bonus puanı kaydedilir.
        await using (var submitContext = db.CreateContext())
        {
            var submitService =
                CreateCountryPuzzleService(submitContext);

            var result =
                await submitService.SubmitBonusAsync(userId);

            Assert.True(result.IsCompleted);
        }

        var completedAssignment = await verificationContext
            .BonusPuzzleAssignments
            .AsNoTracking()
            .SingleAsync(x => x.UserId == userId);

        Assert.True(completedAssignment.IsCompleted);
        Assert.NotNull(completedAssignment.PuzzleAttemptId);

        Assert.Equal(
            originalCountry,
            completedAssignment.CountryCode);

        Assert.Equal(
            originalStartedAt,
            completedAssignment.StartedAtUtc);

        var bonusAttempt = await verificationContext
            .PuzzleAttempts
            .AsNoTracking()
            .SingleAsync(
                x => x.Id ==
                    completedAssignment.PuzzleAttemptId!.Value);

        Assert.Equal(
            originalCountry,
            bonusAttempt.CountryCode);

        await AssertCountryBonusOutboxAsync(
            db,
            userId,
            originalCountry!);
    }

    [PostgresFact]
    public async Task Concurrent_country_change_and_bonus_completion_preserve_original_country()
    {
        await using var db =
            await PostgresTestDatabase.CreateAsync();

        var userId = await SeedUserAsync(
            db,
            "TR",
            DateTime.UtcNow.AddHours(-25));

        await PrepareCountryBonusAsync(
            db,
            userId,
            startBonus: true,
            solveBonus: true);

        var gate = new CountryPuzzleRaceInterceptor(
            CountryPuzzleRaceOperation.BonusCompletion);

        using var factory =
            new TestWebApplicationFactory(db, gate);

        using var client = factory.CreateClient();

        await using var puzzleContext =
            db.CreateContext(gate);

        var service =
            CreateCountryPuzzleService(puzzleContext);

        var countryTask = UpdateCountryAsync(
            client,
            userId,
            "DE");

        var submitTask =
            service.SubmitBonusAsync(userId);

        await Task.WhenAll(
            countryTask,
            submitTask)
            .WaitAsync(TimeSpan.FromSeconds(30));

        using var countryResponse = await countryTask;

        var result = await submitTask;

        Assert.Equal(2, gate.ArrivedCount);

        Assert.Equal(
            HttpStatusCode.OK,
            countryResponse.StatusCode);

        Assert.True(result.IsCompleted);

        await using var verificationContext =
            db.CreateContext();

        var user = await verificationContext.Users
            .AsNoTracking()
            .SingleAsync(x => x.Id == userId);

        Assert.Equal("DE", user.CountryCode);

        var assignment = await verificationContext
            .BonusPuzzleAssignments
            .AsNoTracking()
            .SingleAsync(x => x.UserId == userId);

        Assert.True(assignment.IsCompleted);
        Assert.Equal("TR", assignment.CountryCode);
        Assert.NotNull(assignment.PuzzleAttemptId);

        var attempts = await verificationContext
            .PuzzleAttempts
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .ToListAsync();

        Assert.Equal(2, attempts.Count);

        Assert.All(attempts, attempt =>
        {
            Assert.Equal("TR", attempt.CountryCode);
        });

        var bonusAttempt = Assert.Single(
            attempts,
            x => x.Id == assignment.PuzzleAttemptId!.Value);

        Assert.Equal(result.Score, bonusAttempt.Score);
        Assert.Equal(result.Seconds, bonusAttempt.Seconds);

        await AssertCountryBonusOutboxAsync(
            db,
            userId,
            "TR");
    }

    [PostgresFact]
    public async Task Concurrent_country_change_and_repeated_bonus_submission_create_single_score()
    {
        await using var db =
            await PostgresTestDatabase.CreateAsync();

        var userId = await SeedUserAsync(
            db,
            "TR",
            DateTime.UtcNow.AddHours(-25));

        await PrepareCountryBonusAsync(
            db,
            userId,
            startBonus: true,
            solveBonus: true);

        var gate = new CountryPuzzleRaceInterceptor(
            CountryPuzzleRaceOperation.BonusCompletion);

        using var factory =
            new TestWebApplicationFactory(db, gate);

        using var client = factory.CreateClient();

        await using var firstContext =
            db.CreateContext(gate);

        await using var secondContext =
            db.CreateContext(gate);

        var firstService =
            CreateCountryPuzzleService(firstContext);

        var secondService =
            CreateCountryPuzzleService(secondContext);

        var startGate = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var countryTask = Task.Run(async () =>
        {
            await startGate.Task;

            return await UpdateCountryAsync(
                client,
                userId,
                "DE");
        });

        var firstTask = Task.Run(async () =>
        {
            await startGate.Task;

            return await firstService.SubmitBonusAsync(userId);
        });

        var secondTask = Task.Run(async () =>
        {
            await startGate.Task;

            return await secondService.SubmitBonusAsync(userId);
        });

        startGate.SetResult(true);

        await Task.WhenAll(
            countryTask,
            firstTask,
            secondTask)
            .WaitAsync(TimeSpan.FromSeconds(30));

        using var countryResponse = await countryTask;

        var firstResult = await firstTask;
        var secondResult = await secondTask;

        Assert.Equal(2, gate.ArrivedCount);

        Assert.Equal(
            HttpStatusCode.OK,
            countryResponse.StatusCode);

        Assert.True(firstResult.IsCompleted);
        Assert.True(secondResult.IsCompleted);

        Assert.Equal(
            firstResult.Score,
            secondResult.Score);

        Assert.Equal(
            firstResult.Seconds,
            secondResult.Seconds);

        await using var verificationContext =
            db.CreateContext();

        var user = await verificationContext.Users
            .AsNoTracking()
            .SingleAsync(x => x.Id == userId);

        Assert.Equal("DE", user.CountryCode);

        var assignment = await verificationContext
            .BonusPuzzleAssignments
            .AsNoTracking()
            .SingleAsync(x => x.UserId == userId);

        Assert.True(assignment.IsCompleted);
        Assert.Equal("TR", assignment.CountryCode);
        Assert.NotNull(assignment.PuzzleAttemptId);

        var attempts = await verificationContext
            .PuzzleAttempts
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .ToListAsync();

        // Bir günlük puzzle ve yalnızca bir bonus.
        Assert.Equal(2, attempts.Count);

        Assert.All(attempts, attempt =>
        {
            Assert.Equal("TR", attempt.CountryCode);
        });

        var bonusAttempt = Assert.Single(
            attempts,
            x => x.PuzzleId == assignment.PuzzleId);

        Assert.Equal(
            assignment.PuzzleAttemptId!.Value,
            bonusAttempt.Id);

        Assert.Equal(
            firstResult.Score,
            bonusAttempt.Score);

        Assert.Equal(
            firstResult.Seconds,
            bonusAttempt.Seconds);

        await AssertCountryBonusOutboxAsync(
            db,
            userId,
            "TR");
    }

    [PostgresFact]
    public async Task Expired_bonus_after_country_change_preserves_original_country()
    {
        await using var db =
            await PostgresTestDatabase.CreateAsync();

        var userId = await SeedUserAsync(
            db,
            "TR",
            DateTime.UtcNow.AddHours(-25));

        await PrepareCountryBonusAsync(
            db,
            userId,
            startBonus: true);

        using var factory =
            new TestWebApplicationFactory(db);

        using var client = factory.CreateClient();

        using var countryResponse =
            await UpdateCountryAsync(client, userId, "DE");

        Assert.Equal(
            HttpStatusCode.OK,
            countryResponse.StatusCode);

        // Bonus süresinin dolmasını simüle et.
        await SetCountryBonusTimingAsync(
            db,
            userId,
            solved: false);

        await using (var context = db.CreateContext())
        {
            var service =
                CreateCountryPuzzleService(context);

            var result = await service.SubmitBonusAsync(userId);

            Assert.True(result.IsCompleted);
            Assert.Equal(0, result.Score);
            Assert.Equal(300, result.Seconds);
        }

        await AssertExpiredCountryBonusAsync(db, userId);
    }

    [PostgresFact]
    public async Task Repeated_expired_bonus_submission_after_country_change_does_not_create_score()
    {
        await using var db =
            await PostgresTestDatabase.CreateAsync();

        var userId = await SeedUserAsync(
            db,
            "TR",
            DateTime.UtcNow.AddHours(-25));

        await PrepareCountryBonusAsync(
            db,
            userId,
            startBonus: true);

        await SetCountryBonusTimingAsync(
            db,
            userId,
            solved: false);

        // İlk sonuçlandırma Türkiye'deyken yapılır.
        await using (var firstContext = db.CreateContext())
        {
            var service =
                CreateCountryPuzzleService(firstContext);

            var firstResult =
                await service.SubmitBonusAsync(userId);

            Assert.True(firstResult.IsCompleted);
            Assert.Equal(0, firstResult.Score);
            Assert.Equal(300, firstResult.Seconds);
        }

        using var factory =
            new TestWebApplicationFactory(db);

        using var client = factory.CreateClient();

        using var countryResponse =
            await UpdateCountryAsync(client, userId, "DE");

        Assert.Equal(
            HttpStatusCode.OK,
            countryResponse.StatusCode);

        // Ülke değişikliğinden sonra tekrar sonuçlandırılır.
        for (var i = 0; i < 3; i++)
        {
            await using var context = db.CreateContext();

            var service =
                CreateCountryPuzzleService(context);

            var result =
                await service.SubmitBonusAsync(userId);

            Assert.True(result.IsCompleted);
            Assert.Equal(0, result.Score);
            Assert.Equal(300, result.Seconds);
        }

        await AssertExpiredCountryBonusAsync(db, userId);
    }

    [PostgresFact]
    public async Task Solved_bonus_submitted_after_expiration_and_country_change_preserves_score()
    {
        await using var db =
            await PostgresTestDatabase.CreateAsync();

        var userId = await SeedUserAsync(
            db,
            "TR",
            DateTime.UtcNow.AddHours(-25));

        await PrepareCountryBonusAsync(
            db,
            userId,
            startBonus: true,
            solveBonus: true);

        using var factory =
            new TestWebApplicationFactory(db);

        using var client = factory.CreateClient();

        using var countryResponse =
            await UpdateCountryAsync(client, userId, "DE");

        Assert.Equal(
            HttpStatusCode.OK,
            countryResponse.StatusCode);

        // Bonus 60 saniyede çözülmüş ancak
        // sonuçlandırma 6 dakika sonra yapılmış olsun.
        await SetCountryBonusTimingAsync(
            db,
            userId,
            solved: true);

        const int expectedSeconds = 60;
        const decimal expectedScore = 788.2353m;

        await using (var submitContext = db.CreateContext())
        {
            var service =
                CreateCountryPuzzleService(submitContext);

            var result =
                await service.SubmitBonusAsync(userId);

            Assert.True(result.IsCompleted);

            Assert.Equal(
                expectedSeconds,
                result.Seconds);

            Assert.Equal(
                expectedScore,
                result.Score);
        }

        // Tekrar sonuçlandırma aynı puanı döndürmeli.
        await using (var repeatContext = db.CreateContext())
        {
            var service =
                CreateCountryPuzzleService(repeatContext);

            var result =
                await service.SubmitBonusAsync(userId);

            Assert.True(result.IsCompleted);

            Assert.Equal(
                expectedSeconds,
                result.Seconds);

            Assert.Equal(
                expectedScore,
                result.Score);
        }

        await using var verificationContext =
            db.CreateContext();

        var user = await verificationContext.Users
            .AsNoTracking()
            .SingleAsync(x => x.Id == userId);

        Assert.Equal("DE", user.CountryCode);

        var assignment = await verificationContext
            .BonusPuzzleAssignments
            .AsNoTracking()
            .SingleAsync(x => x.UserId == userId);

        Assert.True(assignment.IsCompleted);
        Assert.Equal("TR", assignment.CountryCode);

        Assert.NotNull(assignment.StartedAtUtc);
        Assert.NotNull(assignment.CompletedAtUtc);
        Assert.NotNull(assignment.PuzzleAttemptId);

        Assert.Equal(
            TimeSpan.FromSeconds(expectedSeconds),
            assignment.CompletedAtUtc!.Value -
            assignment.StartedAtUtc!.Value);

        var bonusAttempt = await verificationContext
            .PuzzleAttempts
            .AsNoTracking()
            .SingleAsync(
                x => x.Id ==
                    assignment.PuzzleAttemptId!.Value);

        Assert.Equal("TR", bonusAttempt.CountryCode);
        Assert.Equal(expectedScore, bonusAttempt.Score);
        Assert.Equal(expectedSeconds, bonusAttempt.Seconds);
        Assert.Equal(0, bonusAttempt.WrongMoves);
        Assert.False(bonusAttempt.UsedHint);

        await AssertCountryBonusOutboxAsync(
            db,
            userId,
            "TR");
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

    private static PuzzleService CreateCountryPuzzleService(
    ApplicationDbContext context)
    {
        return new PuzzleService(
            context,
            NullLogger<PuzzleService>.Instance,
            new NoOpCountryDailyPuzzleService());
    }

    private sealed class NoOpCountryDailyPuzzleService
        : IDailyPuzzleService
    {
        public Task AssignDailyPuzzleAsync(
            DateOnly puzzleDate,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private static async Task PrepareCountryBonusAsync(
    PostgresTestDatabase db,
    int userId,
    bool startBonus = false,
    bool solveBonus = false)
    {
        await db.SeedDailyPuzzleAsync(
            PuzzleAudience.Registered);

        await using (var seedContext = db.CreateContext())
        {
            seedContext.Puzzles.Add(new Puzzle
            {
                LichessPuzzleId = "country-race-bonus",
                Fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
                Moves = "e2e4 e7e5",
                Rating = 2600,
                Popularity = 100,
                Themes = "opening",
                IsActive = true
            });

            await seedContext.SaveChangesAsync();
        }

        await using (var dailyContext = db.CreateContext())
        {
            var service =
                CreateCountryPuzzleService(dailyContext);

            var started = await service.StartSessionAsync(
                userId,
                null);

            Assert.NotNull(started.SessionId);

            var completed = await service.SubmitMoveAsync(
                new PuzzleMoveRequest
                {
                    SessionId = started.SessionId!.Value,
                    Uci = "e7e5",
                    ExpectedMoveIndex = 1
                },
                userId);

            Assert.True(completed.IsCompleted);
            Assert.Equal(0, completed.WrongMoves);
            Assert.False(completed.UsedHint);
        }

        await using (var verificationContext = db.CreateContext())
        {
            var assignment = await verificationContext
                .BonusPuzzleAssignments
                .AsNoTracking()
                .SingleAsync(x => x.UserId == userId);

            Assert.Null(assignment.StartedAtUtc);
            Assert.Null(assignment.CountryCode);
            Assert.False(assignment.IsCompleted);
        }

        if (startBonus)
        {
            await using var bonusContext =
                db.CreateContext();

            var service =
                CreateCountryPuzzleService(bonusContext);

            var started = await service.StartBonusAsync(userId);

            Assert.True(started.IsEligible);
            Assert.NotNull(started.StartedAtUtc);
        }

        if (solveBonus)
        {
            await using var moveContext =
                db.CreateContext();

            var service =
                CreateCountryPuzzleService(moveContext);

            var result = await service.SubmitBonusMoveAsync(
                new PuzzleMoveRequest
                {
                    Uci = "e7e5",
                    ExpectedMoveIndex = 1
                },
                userId);

            Assert.True(result.IsSolved);
        }
    }

    private static async Task AssertCountryBonusOutboxAsync(
    PostgresTestDatabase db,
    int userId,
    string bonusCountry)
    {
        await using var context = db.CreateContext();

        var assignment = await context
            .BonusPuzzleAssignments
            .AsNoTracking()
            .SingleAsync(x => x.UserId == userId);

        Assert.NotNull(assignment.PuzzleAttemptId);

        var attempts = await context.PuzzleAttempts
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .ToListAsync();

        Assert.Equal(2, attempts.Count);

        var bonusAttempt = Assert.Single(
            attempts,
            x => x.Id == assignment.PuzzleAttemptId!.Value);

        var dailyAttempt = Assert.Single(
            attempts,
            x => x.Id != bonusAttempt.Id);

        Assert.Equal("TR", dailyAttempt.CountryCode);
        Assert.Equal(bonusCountry, bonusAttempt.CountryCode);

        var outboxes = await context
            .LeaderboardCacheOutboxes
            .AsNoTracking()
            .ToListAsync();

        Assert.Equal(2, outboxes.Count);

        Assert.Single(
            outboxes,
            x => x.PuzzleAttemptId == dailyAttempt.Id);

        Assert.Single(
            outboxes,
            x => x.PuzzleAttemptId == bonusAttempt.Id);

        var deliveries = await context
            .LeaderboardCacheOutboxDeliveries
            .AsNoTracking()
            .ToListAsync();

        Assert.Equal(16, deliveries.Count);

        Assert.All(deliveries, delivery =>
        {
            Assert.Equal(userId, delivery.TargetUserId);
            Assert.False(delivery.IsRemoval);
        });

        var globalDeliveries = deliveries
            .Where(x => x.CountryCode == null)
            .ToList();

        Assert.Equal(8, globalDeliveries.Count);

        var expectedPeriods = new[]
        {
        "daily",
        "weekly",
        "monthly",
        "alltime"
    };

        foreach (var period in expectedPeriods)
        {
            var initialGlobal = Assert.Single(
                globalDeliveries,
                x => x.Period == period &&
                     x.SolvedCount == 1);

            Assert.Equal(
                dailyAttempt.Score,
                initialGlobal.TotalScore);

            var finalGlobal = Assert.Single(
                globalDeliveries,
                x => x.Period == period &&
                     x.SolvedCount == 2);

            Assert.Equal(
                dailyAttempt.Score + bonusAttempt.Score,
                finalGlobal.TotalScore);

            if (bonusCountry == "TR")
            {
                var initialCountry = Assert.Single(
                    deliveries,
                    x => x.Period == period &&
                         x.CountryCode == "TR" &&
                         x.SolvedCount == 1);

                Assert.Equal(
                    dailyAttempt.Score,
                    initialCountry.TotalScore);

                var finalCountry = Assert.Single(
                    deliveries,
                    x => x.Period == period &&
                         x.CountryCode == "TR" &&
                         x.SolvedCount == 2);

                Assert.Equal(
                    dailyAttempt.Score + bonusAttempt.Score,
                    finalCountry.TotalScore);
            }
            else
            {
                var dailyCountry = Assert.Single(
                    deliveries,
                    x => x.Period == period &&
                         x.CountryCode == "TR");

                Assert.Equal(
                    dailyAttempt.Score,
                    dailyCountry.TotalScore);

                Assert.Equal(1, dailyCountry.SolvedCount);

                var bonusCountryDelivery = Assert.Single(
                    deliveries,
                    x => x.Period == period &&
                         x.CountryCode == bonusCountry);

                Assert.Equal(
                    bonusAttempt.Score,
                    bonusCountryDelivery.TotalScore);

                Assert.Equal(
                    1,
                    bonusCountryDelivery.SolvedCount);
            }
        }

        if (bonusCountry == "TR")
        {
            Assert.Equal(
                8,
                deliveries.Count(x => x.CountryCode == "TR"));

            Assert.DoesNotContain(
                deliveries,
                x => x.CountryCode == "DE");
        }
        else
        {
            Assert.Equal(
                4,
                deliveries.Count(x => x.CountryCode == "TR"));

            Assert.Equal(
                4,
                deliveries.Count(
                    x => x.CountryCode == bonusCountry));
        }
    }

    private static async Task SetCountryBonusTimingAsync(
    PostgresTestDatabase db,
    int userId,
    bool solved)
    {
        await using var context = db.CreateContext();

        var assignment = await context
            .BonusPuzzleAssignments
            .SingleAsync(x => x.UserId == userId);

        Assert.NotNull(assignment.StartedAtUtc);
        Assert.False(assignment.IsCompleted);
        Assert.Null(assignment.PuzzleAttemptId);

        Assert.Equal(
            solved,
            assignment.CompletedAtUtc.HasValue);

        var startedAt = DateTime.UtcNow.AddMinutes(-6);

        assignment.StartedAtUtc = startedAt;

        assignment.CompletedAtUtc = solved
            ? startedAt.AddSeconds(60)
            : null;

        assignment.TotalElapsedMilliseconds = solved ? 60_000L : null;
        assignment.CompensatedOpponentMoves = 0;
        assignment.ServerCompensationMilliseconds = 0;
        assignment.AnimationCompensationMilliseconds = null;
        assignment.AdjustedElapsedMilliseconds = null;
        assignment.NextMoveAllowedAtUtc = null;

        await context.SaveChangesAsync();
    }

    private static async Task AssertExpiredCountryBonusAsync(
    PostgresTestDatabase db,
    int userId)
    {
        await using var context = db.CreateContext();

        var user = await context.Users
            .AsNoTracking()
            .SingleAsync(x => x.Id == userId);

        Assert.Equal("DE", user.CountryCode);

        var assignment = await context
            .BonusPuzzleAssignments
            .AsNoTracking()
            .SingleAsync(x => x.UserId == userId);

        Assert.True(assignment.IsCompleted);
        Assert.Equal("TR", assignment.CountryCode);

        Assert.NotNull(assignment.StartedAtUtc);
        Assert.Null(assignment.CompletedAtUtc);
        Assert.Null(assignment.PuzzleAttemptId);

        Assert.True(
            DateTime.UtcNow -
            assignment.StartedAtUtc!.Value
            >= TimeSpan.FromSeconds(300));

        var attempts = await context.PuzzleAttempts
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .ToListAsync();

        // Yalnızca günlük puzzle puanı bulunmalı.
        var dailyAttempt = Assert.Single(attempts);

        Assert.Equal("TR", dailyAttempt.CountryCode);

        Assert.NotEqual(
            assignment.PuzzleId,
            dailyAttempt.PuzzleId);

        var outboxes = await context
            .LeaderboardCacheOutboxes
            .AsNoTracking()
            .ToListAsync();

        var outbox = Assert.Single(outboxes);

        Assert.Equal(
            dailyAttempt.Id,
            outbox.PuzzleAttemptId);

        var deliveries = await context
            .LeaderboardCacheOutboxDeliveries
            .AsNoTracking()
            .ToListAsync();

        Assert.Equal(8, deliveries.Count);

        Assert.DoesNotContain(
            deliveries,
            x => x.CountryCode == "DE");

        var periods = new[]
        {
        "daily",
        "weekly",
        "monthly",
        "alltime"
    };

        foreach (var period in periods)
        {
            var globalDelivery = Assert.Single(
                deliveries,
                x => x.Period == period &&
                     x.CountryCode == null);

            var countryDelivery = Assert.Single(
                deliveries,
                x => x.Period == period &&
                     x.CountryCode == "TR");

            Assert.Equal(
                dailyAttempt.Score,
                globalDelivery.TotalScore);

            Assert.Equal(
                dailyAttempt.Score,
                countryDelivery.TotalScore);

            Assert.Equal(1, globalDelivery.SolvedCount);
            Assert.Equal(1, countryDelivery.SolvedCount);
        }

        Assert.All(deliveries, delivery =>
        {
            Assert.Equal(userId, delivery.TargetUserId);
            Assert.False(delivery.IsRemoval);
        });
    }
}
