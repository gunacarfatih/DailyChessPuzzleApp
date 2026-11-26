using DailyChessPuzzleApi.Data;
using DailyChessPuzzleApi.DTOs;
using DailyChessPuzzleApi.Models;
using DailyChessPuzzleApi.Services;
using DailyChessPuzzleApi.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DailyChessPuzzleApi.Tests;

[Collection("Postgres integration")]
public sealed class LeaderboardCountryIntegrationTests
{
    [PostgresFact]
    public async Task Global_leaderboard_includes_all_country_scores()
    {
        await using var database =
            await PostgresTestDatabase.CreateAsync();

        var userId = await SeedCountryScoresAsync(database);

        await using var context = database.CreateContext();

        var service = CreateService(context);

        var result = await service.GetLeaderboardAsync(
            country: null,
            userId: userId,
            limit: 10,
            startDate: null,
            endDate: null,
            period: "alltime");

        AssertPeriod(result.AllTime, 1950, 3);
    }

    [PostgresFact]
    public async Task Country_leaderboards_preserve_independent_scores()
    {
        await using var database =
            await PostgresTestDatabase.CreateAsync();

        var userId = await SeedCountryScoresAsync(database);

        await using var context = database.CreateContext();

        var service = CreateService(context);

        var turkey = await service.GetLeaderboardAsync(
            country: "TR",
            userId: userId,
            limit: 10,
            startDate: null,
            endDate: null,
            period: "alltime");

        var germany = await service.GetLeaderboardAsync(
            country: "DE",
            userId: userId,
            limit: 10,
            startDate: null,
            endDate: null,
            period: "alltime");

        var unitedKingdom = await service.GetLeaderboardAsync(
            country: "GB",
            userId: userId,
            limit: 10,
            startDate: null,
            endDate: null,
            period: "alltime");

        AssertPeriod(turkey.AllTime, 800, 1);
        AssertPeriod(germany.AllTime, 650, 1);

        Assert.Empty(unitedKingdom.AllTime.Rankings);
    }

    [PostgresFact]
    public async Task Country_change_does_not_move_historical_scores()
    {
        await using var database =
            await PostgresTestDatabase.CreateAsync();

        var userId = await SeedCountryScoresAsync(database);

        await using (var context = database.CreateContext())
        {
            var user = await context.Users
                .SingleAsync(x => x.Id == userId);

            user.CountryCode = "GB";
            user.CountryChangedAtUtc = DateTime.UtcNow;
            user.UpdatedAtUtc = DateTime.UtcNow;

            await context.SaveChangesAsync();
        }

        await using var verificationContext =
            database.CreateContext();

        var service = CreateService(verificationContext);

        var global = await service.GetLeaderboardAsync(
            null, userId, 10, null, null, "alltime");

        var turkey = await service.GetLeaderboardAsync(
            "TR", userId, 10, null, null, "alltime");

        var germany = await service.GetLeaderboardAsync(
            "DE", userId, 10, null, null, "alltime");

        var unitedKingdom = await service.GetLeaderboardAsync(
            "GB", userId, 10, null, null, "alltime");

        AssertPeriod(global.AllTime, 1950, 3);
        AssertPeriod(turkey.AllTime, 800, 1);
        AssertPeriod(germany.AllTime, 650, 1);

        Assert.Empty(unitedKingdom.AllTime.Rankings);

        var attempts = await verificationContext.PuzzleAttempts
            .AsNoTracking()
            .ToListAsync();

        Assert.Equal(3, attempts.Count);

        Assert.Equal(
            800,
            Assert.Single(
                attempts,
                x => x.CountryCode == "TR").Score);

        Assert.Equal(
            650,
            Assert.Single(
                attempts,
                x => x.CountryCode == "DE").Score);

        Assert.Equal(
            500,
            Assert.Single(
                attempts,
                x => x.CountryCode == null).Score);

        Assert.DoesNotContain(
            attempts,
            x => x.CountryCode == "GB");
    }

    [PostgresFact]
    public async Task Custom_range_filters_scores_by_original_country()
    {
        await using var database =
            await PostgresTestDatabase.CreateAsync();

        var userId = await SeedCountryScoresAsync(database);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var startDate = today.AddDays(-3);
        var endDate = today.AddDays(-1);

        await using var context = database.CreateContext();

        var service = CreateService(context);

        var global = await service.GetLeaderboardAsync(
            null,
            userId,
            10,
            startDate,
            endDate,
            "alltime");

        var turkey = await service.GetLeaderboardAsync(
            "TR",
            userId,
            10,
            startDate,
            endDate,
            "alltime");

        var germany = await service.GetLeaderboardAsync(
            "DE",
            userId,
            10,
            startDate,
            endDate,
            "alltime");

        var unitedKingdom = await service.GetLeaderboardAsync(
            "GB",
            userId,
            10,
            startDate,
            endDate,
            "alltime");

        Assert.NotNull(global.CustomRange);
        Assert.NotNull(turkey.CustomRange);
        Assert.NotNull(germany.CustomRange);
        Assert.NotNull(unitedKingdom.CustomRange);

        AssertPeriod(global.CustomRange!, 1950, 3);
        AssertPeriod(turkey.CustomRange!, 800, 1);
        AssertPeriod(germany.CustomRange!, 650, 1);

        Assert.Empty(
            unitedKingdom.CustomRange!.Rankings);
    }

    [PostgresFact]
    public async Task Custom_range_excludes_attempts_outside_selected_dates()
    {
        await using var database =
            await PostgresTestDatabase.CreateAsync();

        var userId = await SeedCountryScoresAsync(database);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var selectedDate = today.AddDays(-2);

        await using var context = database.CreateContext();

        var service = CreateService(context);

        var global = await service.GetLeaderboardAsync(
            null,
            userId,
            10,
            selectedDate,
            selectedDate,
            "alltime");

        var germany = await service.GetLeaderboardAsync(
            "DE",
            userId,
            10,
            selectedDate,
            selectedDate,
            "alltime");

        var turkey = await service.GetLeaderboardAsync(
            "TR",
            userId,
            10,
            selectedDate,
            selectedDate,
            "alltime");

        Assert.NotNull(global.CustomRange);
        Assert.NotNull(germany.CustomRange);
        Assert.NotNull(turkey.CustomRange);

        AssertPeriod(global.CustomRange!, 650, 1);
        AssertPeriod(germany.CustomRange!, 650, 1);

        Assert.Empty(turkey.CustomRange!.Rankings);
    }

    // ---------------------------------------------------------
    // TEST HELPERS
    // ---------------------------------------------------------

    private static void AssertPeriod(
        LeaderboardPeriodDto period,
        int expectedScore,
        int expectedSolvedCount)
    {
        var ranking = Assert.Single(period.Rankings);

        Assert.Equal(1, ranking.Rank);
        Assert.Equal(expectedScore, ranking.TotalScore);
        Assert.Equal(expectedSolvedCount, ranking.SolvedCount);

        Assert.NotNull(period.CurrentUser);

        Assert.Equal(
            expectedScore,
            period.CurrentUser!.TotalScore);

        Assert.Equal(
            expectedSolvedCount,
            period.CurrentUser.SolvedCount);

        Assert.Equal(
            ranking.PublicId,
            period.CurrentUser.PublicId);

        Assert.Equal(
            ranking.Rank,
            period.CurrentUser.Rank);
    }

    private static LeaderboardService CreateService(
        ApplicationDbContext context)
    {
        var publicIdService = new PublicIdService(
            Options.Create(new PublicIdSettings
            {
                Secret = new string('p', 64)
            }));

        return new LeaderboardService(
            context,
            cacheService: null!,
            publicIdService: publicIdService,
            logger: NullLogger<LeaderboardService>.Instance,
            options: Options.Create(new LeaderboardSettings()));
    }

    private static async Task<int> SeedCountryScoresAsync(
        PostgresTestDatabase database)
    {
        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);

        await using var context = database.CreateContext();

        var user = new User
        {
            Username = "leaderboard-country-user",
            FirstName = "Leaderboard",
            LastName = "Test",
            Email = "leaderboard-country@example.test",
            EmailVerified = true,
            CountryCode = "DE",
            CountryChangedAtUtc = now,
            CreatedAtUtc = now
        };

        context.Users.Add(user);

        await context.SaveChangesAsync();

        var countries = new string?[]
        {
            "TR",
            "DE",
            null
        };

        var points = new[]
        {
            800,
            650,
            500
        };

        for (var i = 0; i < countries.Length; i++)
        {
            var puzzle = new Puzzle
            {
                LichessPuzzleId = $"leaderboard-country-{i}",
                Fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
                Moves = "e2e4 e7e5",
                Rating = 1200,
                Popularity = 100,
                Themes = "opening",
                IsActive = true
            };

            var attempt = new PuzzleAttempt
            {
                Puzzle = puzzle,
                UserId = user.Id,
                CountryCode = countries[i],
                Seconds = (1000 - points[i]) / 2,
                WrongMoves = 0,
                UsedHint = false,
                Score = points[i],
                PlayedOn = today.AddDays(i - 3),
                SubmittedAtUtc = now.AddDays(i - 3)
            };

            context.Puzzles.Add(puzzle);
            context.PuzzleAttempts.Add(attempt);
        }

        context.LeaderboardScores.AddRange(
            CreateScore(user.Id, null, 1950, 3),
            CreateScore(user.Id, "TR", 800, 1),
            CreateScore(user.Id, "DE", 650, 1));

        await context.SaveChangesAsync();

        return user.Id;
    }

    private static LeaderboardScore CreateScore(
        int userId,
        string? countryCode,
        int totalScore,
        int solvedCount)
    {
        return new LeaderboardScore
        {
            ClientId = $"user:{userId}",
            UserId = userId,
            CountryCode = countryCode,
            Period = "alltime",
            PeriodKey = "all",
            TotalScore = totalScore,
            SolvedCount = solvedCount,
            Revision = 1,
            UpdatedAtUtc = DateTime.UtcNow
        };
    }
}