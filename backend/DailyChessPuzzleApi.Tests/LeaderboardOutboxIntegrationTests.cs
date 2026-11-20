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
            hubContext: null!);

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
