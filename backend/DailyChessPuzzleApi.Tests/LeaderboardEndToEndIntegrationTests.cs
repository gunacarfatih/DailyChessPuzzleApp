using System.Text.Json;
using System.Threading.Channels;

using DailyChessPuzzleApi.Data;
using DailyChessPuzzleApi.DTOs;
using DailyChessPuzzleApi.Hubs;
using DailyChessPuzzleApi.Models;
using DailyChessPuzzleApi.Services;
using DailyChessPuzzleApi.Services.Interfaces;

using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace DailyChessPuzzleApi.Tests;

[Collection("Postgres integration")]
public sealed class LeaderboardEndToEndIntegrationTests
{
    private static readonly TimeSpan ReceiveTimeout =
        TimeSpan.FromSeconds(10);

    private static readonly TimeSpan NoMessageTimeout =
        TimeSpan.FromSeconds(1);

    [PostgresRedisFact]
    public async Task Daily_completion_updates_postgres_redis_and_signalr_once()
    {
        await using var database =
            await PostgresTestDatabase.CreateAsync();

        await using var redis =
            await RedisTestDatabase.CreateAsync();

        using var factory =
            new TestWebApplicationFactory(
                database,
                RedisTestDatabase.ConnectionString);

        var userId = await SeedScenarioAsync(
            database, "TR");

        await using var global =
            CreateConnection(factory);

        await using var turkey =
            CreateConnection(factory);

        await using var germany =
            CreateConnection(factory);

        var globalMessages = Listen(global);
        var turkeyMessages = Listen(turkey);
        var germanyMessages = Listen(germany);

        await StartAndJoinAsync(global, null);
        await StartAndJoinAsync(turkey, "TR");
        await StartAndJoinAsync(germany, "DE");

        var score = await CompleteDailyAsync(
            database, userId);

        var periodKey = await IsolateDeliveriesAsync(
            database, redis);

        var cache = redis.CreateCacheService();

        await ProcessOutboxAsync(
            database, factory, cache);

        // Global aboneler global bildirim alir.
        await AssertNotificationAsync(
            globalMessages, periodKey);

        // TR aboneleri global ve TR bildirimi alir.
        await AssertNotificationAsync(
            turkeyMessages, periodKey);

        await AssertNotificationAsync(
            turkeyMessages, periodKey);

        // DE aboneleri yalnizca global bildirim alir.
        await AssertNotificationAsync(
            germanyMessages, periodKey);

        await Task.WhenAll(
            AssertNoNotificationAsync(globalMessages),
            AssertNoNotificationAsync(turkeyMessages),
            AssertNoNotificationAsync(germanyMessages));

        await AssertLeaderboardAsync(
            database, cache, userId,
            null, score, 1);

        await AssertLeaderboardAsync(
            database, cache, userId,
            "TR", score, 1);

        await AssertMissingCountryAsync(
            database, userId, "DE");

        await AssertCountsAsync(
            database,
            expectedAttempts: 1,
            expectedOutboxes: 1,
            expectedDeliveries: 8,
            expectedScores: 8);

        var before = await ReadDailyVersionsAsync(
            database, cache);

        // Tamamlanmis Outbox tekrar calistiriliyor.
        await ProcessOutboxAsync(
            database, factory, cache);

        var after = await ReadDailyVersionsAsync(
            database, cache);

        Assert.Equal(before.Count, after.Count);

        foreach (var entry in before)
        {
            Assert.Equal(
                entry.Value,
                after[entry.Key]);
        }

        await AssertLeaderboardAsync(
            database, cache, userId,
            null, score, 1);

        await AssertLeaderboardAsync(
            database, cache, userId,
            "TR", score, 1);

        await AssertCountsAsync(
            database, 1, 1, 8, 8);

        await Task.WhenAll(
            AssertNoNotificationAsync(globalMessages),
            AssertNoNotificationAsync(turkeyMessages),
            AssertNoNotificationAsync(germanyMessages));
    }

    [PostgresRedisFact]
    public async Task Daily_and_bonus_scores_remain_independent_across_countries()
    {
        await using var database =
            await PostgresTestDatabase.CreateAsync();

        await using var redis =
            await RedisTestDatabase.CreateAsync();

        using var factory =
            new TestWebApplicationFactory(
                database,
                RedisTestDatabase.ConnectionString);

        var userId = await SeedScenarioAsync(
            database, "TR");

        await using var global =
            CreateConnection(factory);

        await using var turkey =
            CreateConnection(factory);

        await using var germany =
            CreateConnection(factory);

        var globalMessages = Listen(global);
        var turkeyMessages = Listen(turkey);
        var germanyMessages = Listen(germany);

        await StartAndJoinAsync(global, null);
        await StartAndJoinAsync(turkey, "TR");
        await StartAndJoinAsync(germany, "DE");

        // Gunluk puzzle TR ulkesinde tamamlanir.
        var dailyScore = await CompleteDailyAsync(
            database, userId);

        // Test icin ulke dogrudan degistirilir.
        await SetCountryAsync(
            database, userId, "DE");

        // Bonus puzzle DE ulkesinde tamamlanir.
        var bonusScore = await CompleteBonusAsync(
            database, userId);

        // Sonraki ulke degisikligi eski puanlari
        // etkilememeli.
        await SetCountryAsync(
            database, userId, "GB");

        var periodKey = await IsolateDeliveriesAsync(
            database, redis);

        var cache = redis.CreateCacheService();

        await ProcessOutboxAsync(
            database, factory, cache);

        await AssertLeaderboardAsync(
            database,
            cache,
            userId,
            null,
            dailyScore + bonusScore,
            2);

        await AssertLeaderboardAsync(
            database,
            cache,
            userId,
            "TR",
            dailyScore,
            1);

        await AssertLeaderboardAsync(
            database,
            cache,
            userId,
            "DE",
            bonusScore,
            1);

        await AssertMissingCountryAsync(
            database, userId, "GB");

        await AssertCountsAsync(
            database,
            expectedAttempts: 2,
            expectedOutboxes: 2,
            expectedDeliveries: 16,
            expectedScores: 12);

        await using (var context =
            database.CreateContext())
        {
            var attempts = await context.PuzzleAttempts
                .AsNoTracking()
                .ToListAsync();

            var daily = Assert.Single(
                attempts,
                x => x.CountryCode == "TR");

            var bonus = Assert.Single(
                attempts,
                x => x.CountryCode == "DE");

            Assert.Equal(dailyScore, daily.Score);
            Assert.Equal(bonusScore, bonus.Score);

            var user = await context.Users
                .AsNoTracking()
                .SingleAsync(x => x.Id == userId);

            Assert.Equal("GB", user.CountryCode);
        }

        // Global bildirim throttle nedeniyle
        // bir veya daha fazla kez gelebilir.
        await AssertNotificationAsync(
            globalMessages, periodKey);

        // Her ulke abonesi en az global ve kendi
        // ulkesinin bildirimini almalidir.
        await AssertNotificationAsync(
            turkeyMessages, periodKey);

        await AssertNotificationAsync(
            turkeyMessages, periodKey);

        await AssertNotificationAsync(
            germanyMessages, periodKey);

        await AssertNotificationAsync(
            germanyMessages, periodKey);
    }

    [PostgresRedisFact]
    public async Task Countryless_completion_updates_only_global_leaderboard()
    {
        await using var database =
            await PostgresTestDatabase.CreateAsync();

        await using var redis =
            await RedisTestDatabase.CreateAsync();

        using var factory =
            new TestWebApplicationFactory(
                database,
                RedisTestDatabase.ConnectionString);

        var userId = await SeedScenarioAsync(
            database, null);

        await using var global =
            CreateConnection(factory);

        await using var turkey =
            CreateConnection(factory);

        await using var germany =
            CreateConnection(factory);

        var globalMessages = Listen(global);
        var turkeyMessages = Listen(turkey);
        var germanyMessages = Listen(germany);

        await StartAndJoinAsync(global, null);
        await StartAndJoinAsync(turkey, "TR");
        await StartAndJoinAsync(germany, "DE");

        var score = await CompleteDailyAsync(
            database, userId);

        // Sonradan ulke atanmasi eski puani
        // ulke tablosuna tasimamali.
        await SetCountryAsync(
            database, userId, "TR");

        var periodKey = await IsolateDeliveriesAsync(
            database, redis);

        var cache = redis.CreateCacheService();

        await ProcessOutboxAsync(
            database, factory, cache);

        await AssertLeaderboardAsync(
            database,
            cache,
            userId,
            null,
            score,
            1);

        await AssertMissingCountryAsync(
            database, userId, "TR");

        await AssertMissingCountryAsync(
            database, userId, "DE");

        await AssertCountsAsync(
            database,
            expectedAttempts: 1,
            expectedOutboxes: 1,
            expectedDeliveries: 4,
            expectedScores: 4);

        await using (var context =
            database.CreateContext())
        {
            var attempt = await context.PuzzleAttempts
                .AsNoTracking()
                .SingleAsync();

            Assert.Null(attempt.CountryCode);

            var user = await context.Users
                .AsNoTracking()
                .SingleAsync(x => x.Id == userId);

            Assert.Equal("TR", user.CountryCode);
        }

        // Ulke aboneleri global gruba da uye.
        await AssertNotificationAsync(
            globalMessages, periodKey);

        await AssertNotificationAsync(
            turkeyMessages, periodKey);

        await AssertNotificationAsync(
            germanyMessages, periodKey);

        // Ulkeye ozel ek bildirim olmamali.
        await Task.WhenAll(
            AssertNoNotificationAsync(globalMessages),
            AssertNoNotificationAsync(turkeyMessages),
            AssertNoNotificationAsync(germanyMessages));
    }

    // ---------------------------------------------------------
    // PUZZLE HELPERS
    // ---------------------------------------------------------

    private static async Task<int> SeedScenarioAsync(
        PostgresTestDatabase database,
        string? countryCode)
    {
        await database.SeedDailyPuzzleAsync(
            PuzzleAudience.Registered);

        await using var context =
            database.CreateContext();

        var now = DateTime.UtcNow;

        var user = new User
        {
            Username = "end-to-end-user",
            FirstName = "Integration",
            LastName = "Test",
            Email = "end-to-end@example.test",
            EmailVerified = true,
            CountryCode = countryCode,
            CountryChangedAtUtc =
                countryCode == null ? null : now,
            CreatedAtUtc = now
        };

        var bonusPuzzle = new Puzzle
        {
            LichessPuzzleId = "end-to-end-bonus",
            Fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
            Moves = "e2e4 e7e5",
            Rating = 2600,
            Popularity = 100,
            Themes = "opening",
            IsActive = true
        };

        context.Users.Add(user);
        context.Puzzles.Add(bonusPuzzle);

        await context.SaveChangesAsync();

        return user.Id;
    }

    private static async Task<int> CompleteDailyAsync(
        PostgresTestDatabase database,
        int userId)
    {
        await using var context =
            database.CreateContext();

        var service = CreatePuzzleService(context);

        var session = await service.StartSessionAsync(
            userId, null);

        Assert.NotNull(session.SessionId);

        var result = await service.SubmitMoveAsync(
            new PuzzleMoveRequest
            {
                SessionId = session.SessionId,
                Uci = "e7e5",
                ExpectedMoveIndex = 1
            },
            userId);

        Assert.True(result.IsCompleted);
        Assert.NotNull(result.Score);
        Assert.Equal(0, result.WrongMoves);
        Assert.False(result.UsedHint);

        return result.Score.Value;
    }

    private static async Task<int> CompleteBonusAsync(
        PostgresTestDatabase database,
        int userId)
    {
        await using var context =
            database.CreateContext();

        var service = CreatePuzzleService(context);

        var bonus = await service.StartBonusAsync(
            userId);

        Assert.True(bonus.IsEligible);

        var move = await service.SubmitBonusMoveAsync(
            new PuzzleMoveRequest
            {
                Uci = "e7e5",
                ExpectedMoveIndex = 1
            },
            userId);

        Assert.True(move.IsSolved);

        var result = await service.SubmitBonusAsync(
            userId);

        Assert.True(result.IsCompleted);

        return result.Score;
    }

    private static async Task SetCountryAsync(
        PostgresTestDatabase database,
        int userId,
        string countryCode)
    {
        await using var context =
            database.CreateContext();

        var user = await context.Users
            .SingleAsync(x => x.Id == userId);

        user.CountryCode = countryCode;
        user.CountryChangedAtUtc = DateTime.UtcNow;
        user.UpdatedAtUtc = DateTime.UtcNow;

        await context.SaveChangesAsync();
    }

    private static PuzzleService CreatePuzzleService(
        ApplicationDbContext context)
    {
        return new PuzzleService(
            context,
            NullLogger<PuzzleService>.Instance,
            new NoOpDailyPuzzleService());
    }

    private sealed class NoOpDailyPuzzleService
        : IDailyPuzzleService
    {
        public Task AssignDailyPuzzleAsync(
            DateOnly puzzleDate,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    // ---------------------------------------------------------
    // OUTBOX HELPERS
    // ---------------------------------------------------------

    private static async Task<string> IsolateDeliveriesAsync(
        PostgresTestDatabase database,
        RedisTestDatabase redis)
    {
        await using var context =
            database.CreateContext();

        var deliveries = await context
            .LeaderboardCacheOutboxDeliveries
            .ToListAsync();

        Assert.NotEmpty(deliveries);

        var uniquePeriodKey =
            $"test-{Guid.NewGuid():N}"[..20];

        foreach (var delivery in deliveries)
        {
            // SignalR static notification throttle
            // diger testleri etkilememeli.
            if (delivery.Period == "daily")
            {
                delivery.PeriodKey = uniquePeriodKey;
            }

            // Production cache anahtarlari yerine
            // izole test anahtarlari kullanilir.
            delivery.CacheKey = redis.CreateKey(
                $"{delivery.Period}:" +
                $"{delivery.PeriodKey}:" +
                $"{delivery.CountryCode ?? "global"}");
        }

        await context.SaveChangesAsync();

        return uniquePeriodKey;
    }

    private static async Task ProcessOutboxAsync(
        PostgresTestDatabase database,
        TestWebApplicationFactory factory,
        ILeaderboardCacheService cache)
    {
        await using var context =
            database.CreateContext();

        var hubContext = factory.Services
            .GetRequiredService<
                IHubContext<LeaderboardHub>>();

        var service = new LeaderboardOutboxService(
            context,
            cache,
            NullLogger<LeaderboardOutboxService>.Instance,
            hubContext);

        var hasMore = await service.ProcessPendingAsync(
            CancellationToken.None);

        Assert.False(hasMore);
    }

    // ---------------------------------------------------------
    // POSTGRESQL AND REDIS ASSERTIONS
    // ---------------------------------------------------------

    private static async Task AssertLeaderboardAsync(
        PostgresTestDatabase database,
        ILeaderboardCacheService cache,
        int userId,
        string? countryCode,
        int expectedScore,
        int expectedSolvedCount)
    {
        await using var context =
            database.CreateContext();

        var clientId = $"user:{userId}";

        var scores = await context.LeaderboardScores
            .AsNoTracking()
            .Where(x =>
                x.UserId == userId &&
                x.CountryCode == countryCode)
            .ToListAsync();

        Assert.Equal(4, scores.Count);

        var deliveries = await context
            .LeaderboardCacheOutboxDeliveries
            .AsNoTracking()
            .Where(x => x.CountryCode == countryCode)
            .ToListAsync();

        foreach (var period in new[]
        {
            "daily",
            "weekly",
            "monthly",
            "alltime"
        })
        {
            var score = Assert.Single(
                scores,
                x => x.Period == period);

            Assert.Equal(clientId, score.ClientId);
            Assert.Equal(expectedScore, score.TotalScore);

            Assert.Equal(
                expectedSolvedCount,
                score.SolvedCount);

            Assert.True(score.Revision > 0);

            var delivery = deliveries
                .Where(x =>
                    x.Period == period &&
                    x.PeriodKey == score.PeriodKey)
                .OrderByDescending(x => x.OutboxId)
                .First();

            Assert.Equal(
                delivery.OutboxId,
                score.Revision);

            var redisScore =
                await cache.GetUserScoreAsync(
                    delivery.CacheKey,
                    clientId);

            Assert.NotNull(redisScore);

            Assert.Equal(
                (expectedScore, expectedSolvedCount),
                redisScore.Value);

            var top = Assert.Single(
                await cache.GetTopAsync(
                    delivery.CacheKey, 10));

            Assert.Equal(
                (clientId,
                 expectedScore,
                 expectedSolvedCount),
                top);

            Assert.Equal(
                1L,
                await cache.GetRankAsync(
                    delivery.CacheKey,
                    clientId));
        }
    }

    private static async Task AssertMissingCountryAsync(
        PostgresTestDatabase database,
        int userId,
        string countryCode)
    {
        await using var context =
            database.CreateContext();

        Assert.False(
            await context.LeaderboardScores.AnyAsync(
                x => x.UserId == userId &&
                     x.CountryCode == countryCode));

        Assert.False(
            await context.LeaderboardCacheOutboxDeliveries
                .AnyAsync(
                    x => x.CountryCode == countryCode));
    }

    private static async Task AssertCountsAsync(
        PostgresTestDatabase database,
        int expectedAttempts,
        int expectedOutboxes,
        int expectedDeliveries,
        int expectedScores)
    {
        await using var context =
            database.CreateContext();

        Assert.Equal(
            expectedAttempts,
            await context.PuzzleAttempts.CountAsync());

        Assert.Equal(
            expectedOutboxes,
            await context.LeaderboardCacheOutboxes
                .CountAsync());

        Assert.Equal(
            expectedScores,
            await context.LeaderboardScores.CountAsync());

        var deliveries = await context
            .LeaderboardCacheOutboxDeliveries
            .AsNoTracking()
            .ToListAsync();

        Assert.Equal(
            expectedDeliveries,
            deliveries.Count);

        Assert.All(deliveries, delivery =>
        {
            Assert.Equal(
                "Completed",
                delivery.Status.ToString());

            Assert.NotNull(
                delivery.ProcessedAtUtc);
        });
    }

    private static async Task<Dictionary<string, long>>
        ReadDailyVersionsAsync(
            PostgresTestDatabase database,
            ILeaderboardCacheService cache)
    {
        await using var context =
            database.CreateContext();

        var keys = await context
            .LeaderboardCacheOutboxDeliveries
            .AsNoTracking()
            .Where(x => x.Period == "daily")
            .Select(x => x.CacheKey)
            .Distinct()
            .ToListAsync();

        var versions = new Dictionary<string, long>();

        foreach (var key in keys)
        {
            var versionKey = $"{key}:version";

            var version = await cache.GetVersionAsync(
                versionKey);

            Assert.True(version > 0);

            versions.Add(versionKey, version);
        }

        return versions;
    }

    // ---------------------------------------------------------
    // SIGNALR HELPERS
    // ---------------------------------------------------------

    private static HubConnection CreateConnection(
        TestWebApplicationFactory factory)
    {
        var server = factory.Server;

        var hubUri = new Uri(
            server.BaseAddress,
            "/hubs/leaderboard");

        return new HubConnectionBuilder()
            .WithUrl(hubUri, options =>
            {
                options.Transports =
                    HttpTransportType.LongPolling;

                options.HttpMessageHandlerFactory =
                    _ => server.CreateHandler();
            })
            .Build();
    }

    private static Channel<JsonElement> Listen(
        HubConnection connection)
    {
        var messages =
            Channel.CreateUnbounded<JsonElement>();

        connection.On<JsonElement>(
            "LeaderboardUpdated",
            payload =>
            {
                messages.Writer.TryWrite(
                    payload.Clone());
            });

        return messages;
    }

    private static async Task StartAndJoinAsync(
        HubConnection connection,
        string? countryCode)
    {
        await connection.StartAsync()
            .WaitAsync(ReceiveTimeout);

        await connection.InvokeAsync(
            "JoinLeaderboard",
            "daily",
            countryCode)
            .WaitAsync(ReceiveTimeout);

        Assert.Equal(
            HubConnectionState.Connected,
            connection.State);
    }

    private static async Task AssertNotificationAsync(
        Channel<JsonElement> messages,
        string expectedPeriodKey)
    {
        using var cancellation =
            new CancellationTokenSource(
                ReceiveTimeout);

        var payload = await messages.Reader
            .ReadAsync(cancellation.Token);

        Assert.Equal(
            "daily",
            payload.GetProperty("period")
                .GetString());

        Assert.Equal(
            expectedPeriodKey,
            payload.GetProperty("periodKey")
                .GetString());
    }

    private static async Task AssertNoNotificationAsync(
        Channel<JsonElement> messages)
    {
        using var cancellation =
            new CancellationTokenSource(
                NoMessageTimeout);

        await Assert.ThrowsAnyAsync<
            OperationCanceledException>(
            async () =>
            {
                await messages.Reader.ReadAsync(
                    cancellation.Token);
            });
    }
}