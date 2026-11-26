using DailyChessPuzzleApi.Data;
using DailyChessPuzzleApi.Hubs;
using DailyChessPuzzleApi.Models;
using DailyChessPuzzleApi.Services;
using DailyChessPuzzleApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DailyChessPuzzleApi.Tests;

[Collection("Postgres integration")]
public sealed class LeaderboardOutboxIntegrationTests
{
    [PostgresFact]
    public async Task Pending_delivery_updates_postgres_and_cache_then_completes_once()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);
        int userId;
        int deliveryId;

        await using (var seedContext = database.CreateContext())
        {
            var user = new User
            {
                Username = "outbox-integration",
                FirstName = "Outbox",
                LastName = "Test",
                Email = "outbox@example.test",
                EmailVerified = true,
                CreatedAtUtc = now
            };
            var puzzle = new Puzzle
            {
                LichessPuzzleId = "outbox-test-puzzle",
                Fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
                Moves = "e2e4 e7e5",
                Rating = 1200,
                Popularity = 100,
                Themes = "opening"
            };
            var attempt = new PuzzleAttempt
            {
                Puzzle = puzzle,
                User = user,
                Seconds = 12,
                WrongMoves = 0,
                UsedHint = false,
                Score = 976,
                PlayedOn = today,
                SubmittedAtUtc = now
            };
            seedContext.Users.Add(user);
            seedContext.Puzzles.Add(puzzle);
            seedContext.PuzzleAttempts.Add(attempt);
            await seedContext.SaveChangesAsync();

            var outbox = new LeaderboardCacheOutbox
            {
                PuzzleAttemptId = attempt.Id,
                UserId = user.Id,
                ClientId = $"user:{user.Id}",
                CreatedAtUtc = now
            };
            var delivery = new LeaderboardCacheOutboxDelivery
            {
                Outbox = outbox,
                TargetClientId = $"user:{user.Id}",
                TargetUserId = user.Id,
                Period = "weekly",
                PeriodKey = "test-week",
                CacheKey = "leaderboard:test:weekly",
                TotalScore = 976,
                SolvedCount = 1,
                Status = LeaderboardCacheOutboxDeliveryStatus.Pending,
                CreatedAtUtc = now
            };
            seedContext.LeaderboardCacheOutboxes.Add(outbox);
            seedContext.LeaderboardCacheOutboxDeliveries.Add(delivery);
            await seedContext.SaveChangesAsync();
            userId = user.Id;
            deliveryId = delivery.Id;
        }

        await using var context = database.CreateContext();
        var cache = new RecordingLeaderboardCacheService();
        var service = new LeaderboardOutboxService(
            context,
            cache,
            NullLogger<LeaderboardOutboxService>.Instance,
            publisher: new NoOpLeaderboardPublisher());

        var hasMore = await service.ProcessPendingAsync(CancellationToken.None);
        var score = await context.LeaderboardScores.SingleAsync();
        var deliveryAfterProcessing = await context.LeaderboardCacheOutboxDeliveries
            .SingleAsync(x => x.Id == deliveryId);

        Assert.False(hasMore);
        Assert.Equal(userId, score.UserId);
        Assert.Equal("user:" + userId, score.ClientId);
        Assert.Equal(976, score.TotalScore);
        Assert.Equal(1, score.SolvedCount);
        Assert.Equal(LeaderboardCacheOutboxDeliveryStatus.Completed, deliveryAfterProcessing.Status);
        Assert.Equal("leaderboard:test:weekly", cache.LastUpdatedKey);

        await service.ProcessPendingAsync(CancellationToken.None);
        Assert.Equal(1, await context.LeaderboardScores.CountAsync());
    }

    [PostgresFact]
    public async Task Country_deliveries_preserve_separate_scores_and_global_total()
    {
        await using var database =
            await PostgresTestDatabase.CreateAsync();

        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);

        int userId;

        await using (var context = database.CreateContext())
        {
            var user = new User
            {
                Username = "country-outbox-test",
                FirstName = "Country",
                LastName = "Test",
                Email = "country-outbox@example.test",
                EmailVerified = true,
                CountryCode = "DE",
                CountryChangedAtUtc = now,
                CreatedAtUtc = now
            };

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

            context.Users.Add(user);

            for (int i = 0; i < countries.Length; i++)
            {
                var puzzle = new Puzzle
                {
                    LichessPuzzleId = $"country-outbox-{i}",
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
                    User = user,
                    CountryCode = countries[i],
                    Seconds = (1000 - points[i]) / 2,
                    WrongMoves = 0,
                    UsedHint = false,
                    Score = points[i],
                    PlayedOn = today.AddDays(i - 2),
                    SubmittedAtUtc = now.AddDays(i - 2)
                };

                context.Puzzles.Add(puzzle);
                context.PuzzleAttempts.Add(attempt);
            }

            await context.SaveChangesAsync();

            userId = user.Id;

            var attempts = await context.PuzzleAttempts
                .OrderBy(x => x.Id)
                .ToListAsync();

            var globalTotal = 0;

            for (int i = 0; i < attempts.Count; i++)
            {
                var attempt = attempts[i];

                globalTotal += attempt.Score;

                var outbox = new LeaderboardCacheOutbox
                {
                    PuzzleAttemptId = attempt.Id,
                    UserId = userId,
                    ClientId = $"user:{userId}",
                    CreatedAtUtc = now
                };

                context.LeaderboardCacheOutboxes.Add(outbox);

                context.LeaderboardCacheOutboxDeliveries.Add(
                    new LeaderboardCacheOutboxDelivery
                    {
                        Outbox = outbox,
                        TargetClientId = $"user:{userId}",
                        TargetUserId = userId,
                        CountryCode = null,
                        Period = "alltime",
                        PeriodKey = "all",
                        CacheKey = "leaderboard:test:alltime:global",
                        TotalScore = globalTotal,
                        SolvedCount = i + 1,
                        Status = LeaderboardCacheOutboxDeliveryStatus.Pending,
                        CreatedAtUtc = now
                    });

                if (attempt.CountryCode != null)
                {
                    context.LeaderboardCacheOutboxDeliveries.Add(
                        new LeaderboardCacheOutboxDelivery
                        {
                            Outbox = outbox,
                            TargetClientId = $"user:{userId}",
                            TargetUserId = userId,
                            CountryCode = attempt.CountryCode,
                            Period = "alltime",
                            PeriodKey = "all",
                            CacheKey =
                                $"leaderboard:test:alltime:{attempt.CountryCode}",
                            TotalScore = attempt.Score,
                            SolvedCount = 1,
                            Status = LeaderboardCacheOutboxDeliveryStatus.Pending,
                            CreatedAtUtc = now
                        });
                }

                await context.SaveChangesAsync();
            }
        }

        await using (var context = database.CreateContext())
        {
            var cache = new RecordingLeaderboardCacheService();

            var service = new LeaderboardOutboxService(
                context,
                cache,
                NullLogger<LeaderboardOutboxService>.Instance,
                publisher: new NoOpLeaderboardPublisher());

            var hasMore = await service.ProcessPendingAsync(
                CancellationToken.None);

            Assert.False(hasMore);
        }

        await using (var context = database.CreateContext())
        {
            var scores = await context.LeaderboardScores
                .AsNoTracking()
                .ToListAsync();

            Assert.Equal(3, scores.Count);

            var global = Assert.Single(
                scores,
                x => x.CountryCode == null);

            var turkey = Assert.Single(
                scores,
                x => x.CountryCode == "TR");

            var germany = Assert.Single(
                scores,
                x => x.CountryCode == "DE");

            Assert.Equal(1950, global.TotalScore);
            Assert.Equal(3, global.SolvedCount);

            Assert.Equal(800, turkey.TotalScore);
            Assert.Equal(1, turkey.SolvedCount);

            Assert.Equal(650, germany.TotalScore);
            Assert.Equal(1, germany.SolvedCount);

            Assert.All(scores, score =>
            {
                Assert.Equal(userId, score.UserId);
                Assert.Equal($"user:{userId}", score.ClientId);
            });

            var deliveries = await context
                .LeaderboardCacheOutboxDeliveries
                .AsNoTracking()
                .ToListAsync();

            Assert.Equal(5, deliveries.Count);

            Assert.All(deliveries, delivery =>
            {
                Assert.Equal(
                    LeaderboardCacheOutboxDeliveryStatus.Completed,
                    delivery.Status);
            });

            // Kullanıcının güncel ülkesi değişiyor.
            var user = await context.Users
                .SingleAsync(x => x.Id == userId);

            user.CountryCode = "GB";
            user.CountryChangedAtUtc = DateTime.UtcNow;
            user.UpdatedAtUtc = DateTime.UtcNow;

            await context.SaveChangesAsync();
        }

        // Outbox yeniden çalıştırılıyor.
        await using (var context = database.CreateContext())
        {
            var service = new LeaderboardOutboxService(
                context,
                new RecordingLeaderboardCacheService(),
                NullLogger<LeaderboardOutboxService>.Instance,
                publisher: new NoOpLeaderboardPublisher());

            await service.ProcessPendingAsync(
                CancellationToken.None);
        }

        await using var verificationContext =
            database.CreateContext();

        var finalScores = await verificationContext
            .LeaderboardScores
            .AsNoTracking()
            .ToListAsync();

        Assert.Equal(3, finalScores.Count);

        Assert.Equal(
            1950,
            Assert.Single(
                finalScores,
                x => x.CountryCode == null).TotalScore);

        Assert.Equal(
            800,
            Assert.Single(
                finalScores,
                x => x.CountryCode == "TR").TotalScore);

        Assert.Equal(
            650,
            Assert.Single(
                finalScores,
                x => x.CountryCode == "DE").TotalScore);

        Assert.DoesNotContain(
            finalScores,
            x => x.CountryCode == "GB");

        Assert.Equal(
            3,
            await verificationContext.PuzzleAttempts.CountAsync());

        Assert.Equal(
            3,
            await verificationContext
                .LeaderboardCacheOutboxes.CountAsync());

        Assert.Equal(
            5,
            await verificationContext
                .LeaderboardCacheOutboxDeliveries.CountAsync());
    }

    private sealed class NoOpLeaderboardPublisher
    : ILeaderboardPublisher
    {
        public Task PublishAsync(
            string period,
            string periodKey,
            string? countryCode,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingLeaderboardCacheService : ILeaderboardCacheService
    {
        public string? LastUpdatedKey { get; private set; }

        public Task UpdateScoreAsync(string key, string clientId, int totalScore, int solvedCount, long revision)
        {
            LastUpdatedKey = key;
            return Task.CompletedTask;
        }

        public Task UpdateDailyScoreAsync(string key, string versionKey, string clientId, int totalScore, int solvedCount, long revision) => Task.CompletedTask;
        public Task RemoveScoreAsync(string key, string clientId, long revision) => Task.CompletedTask;
        public Task RemoveDailyScoreAsync(string key, string versionKey, string clientId, long revision) => Task.CompletedTask;
        public Task<bool> IsDailyCacheReadyAsync(string key) => Task.FromResult(false);
        public Task RebuildDailyAsync(string key, List<(string ClientId, int TotalScore, int SolvedCount, long Revision)> entries) => Task.CompletedTask;
        public Task<(int TotalScore, int SolvedCount)?> GetUserScoreAsync(string key, string clientId) => Task.FromResult<(int, int)?>(null);
        public Task<List<(string ClientId, int TotalScore, int SolvedCount)>> GetTopAsync(string key, int count, long offset = 0) => Task.FromResult(new List<(string, int, int)>());
        public Task<long?> GetRankAsync(string key, string clientId) => Task.FromResult<long?>(null);
        public Task<bool> ExistsAsync(string key) => Task.FromResult(true);
        public Task<long> GetVersionAsync(string versionKey) => Task.FromResult(0L);
        public Task<long> IncrementVersionAsync(string versionKey) => Task.FromResult(1L);
        public Task RebuildRangeAsync(string rangeKey, IReadOnlyCollection<string> dailyKeys, TimeSpan cacheDuration) => Task.CompletedTask;
        public Task RebuildAsync(string key, List<(string ClientId, int TotalScore, int SolvedCount, long Revision)> entries) => Task.CompletedTask;
    }
}
