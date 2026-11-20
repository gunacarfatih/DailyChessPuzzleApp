using DailyChessPuzzleApi.Data;
using DailyChessPuzzleApi.DTOs;
using DailyChessPuzzleApi.Models;
using DailyChessPuzzleApi.Services;
using DailyChessPuzzleApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace DailyChessPuzzleApi.Tests;

[Collection("Postgres integration")]
public sealed class PostgresPuzzleServiceTests
{
    [PostgresFact]
    public async Task Guest_session_resumes_counts_wrong_moves_and_finalizes_once()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await database.SeedDailyPuzzleAsync(PuzzleAudience.Guest);
        await using var context = database.CreateContext();
        var service = CreateService(context);

        var firstStart = await service.StartSessionAsync(null, "guest-test");
        var resumedStart = await service.StartSessionAsync(null, "guest-test");

        Assert.Equal(firstStart.SessionId, resumedStart.SessionId);

        var wrongMove = await service.SubmitMoveAsync(
            new PuzzleMoveRequest
            {
                SessionId = firstStart.SessionId,
                ClientId = "guest-test",
                Uci = "d2d4"
            },
            userId: null);

        Assert.Equal(1, wrongMove.WrongMoves);
        Assert.False(wrongMove.IsCompleted);

        var hint = await service.UseHintAsync(
            firstStart.SessionId!.Value,
            userId: null,
            clientId: "guest-test");

        Assert.True(hint.UsedHint);
        Assert.Equal("e7", hint.HintSquare);

        var completed = await service.SubmitMoveAsync(
            new PuzzleMoveRequest
            {
                SessionId = firstStart.SessionId,
                ClientId = "guest-test",
                Uci = "e7e5"
            },
            userId: null);

        Assert.True(completed.IsCompleted);
        Assert.InRange(completed.Score!.Value, 0, 600);

        var replayedCompletion = await service.SubmitMoveAsync(
            new PuzzleMoveRequest
            {
                SessionId = firstStart.SessionId,
                ClientId = "guest-test",
                Uci = "e7e5"
            },
            userId: null);

        Assert.Equal(completed.Score, replayedCompletion.Score);
        Assert.Equal(1, await context.PuzzleAttempts.CountAsync());
    }

    [PostgresFact]
    public async Task Registered_completion_creates_score_outbox_and_bonus_assignment()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var user = new User
        {
            Username = "integration-user",
            FirstName = "Integration",
            LastName = "Test",
            Email = "integration@example.test",
            EmailVerified = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        var dailyPuzzle = CreatePuzzle("daily-registered", rating: 1200);
        var bonusPuzzle = CreatePuzzle("bonus-candidate", rating: 2600);

        await using (var seedContext = database.CreateContext())
        {
            seedContext.Users.Add(user);
            seedContext.Puzzles.AddRange(dailyPuzzle, bonusPuzzle);
            seedContext.DailyPuzzles.Add(new DailyPuzzle
            {
                Puzzle = dailyPuzzle,
                PuzzleDate = today,
                Audience = PuzzleAudience.Registered
            });
            await seedContext.SaveChangesAsync();
        }

        await using var context = database.CreateContext();
        var service = CreateService(context);
        var session = await service.StartSessionAsync(user.Id, null);
        var result = await service.SubmitMoveAsync(
            new PuzzleMoveRequest
            {
                SessionId = session.SessionId,
                Uci = "e7e5"
            },
            user.Id);

        Assert.True(result.IsCompleted);
        Assert.NotNull(result.Score);
        Assert.Equal(1, await context.PuzzleAttempts.CountAsync());
        Assert.Equal(1, await context.LeaderboardCacheOutboxes.CountAsync());
        Assert.Equal(4, await context.LeaderboardCacheOutboxDeliveries.CountAsync());
        Assert.Equal(1, await context.BonusPuzzleAssignments.CountAsync());
    }

    private static PuzzleService CreateService(ApplicationDbContext context) =>
        new(
            context,
            NullLogger<PuzzleService>.Instance,
            new NoOpDailyPuzzleService());

    private static Puzzle CreatePuzzle(string lichessPuzzleId, int rating) =>
        new()
        {
            LichessPuzzleId = lichessPuzzleId,
            Fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
            Moves = "e2e4 e7e5",
            Rating = rating,
            Popularity = 100,
            Themes = "opening",
            IsActive = true
        };

    private sealed class NoOpDailyPuzzleService : IDailyPuzzleService
    {
        public Task AssignDailyPuzzleAsync(
            DateOnly puzzleDate,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

}
