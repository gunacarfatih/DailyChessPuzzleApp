using DailyChessPuzzleApi.Data;
using DailyChessPuzzleApi.DTOs;
using DailyChessPuzzleApi.Exceptions;
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
        await using var database =
            await PostgresTestDatabase.CreateAsync();

        await database.SeedDailyPuzzleAsync(
            PuzzleAudience.Guest);

        await using var context = database.CreateContext();

        var service = CreateService(context);

        var firstStart =
            await service.StartSessionAsync(null, "guest-test");

        var resumedStart =
            await service.StartSessionAsync(null, "guest-test");

        Assert.Equal(
            firstStart.SessionId,
            resumedStart.SessionId);

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
        Assert.InRange(completed.Score!.Value, 100m, 1000m);

        var replayedCompletion = await service.SubmitMoveAsync(
            new PuzzleMoveRequest
            {
                SessionId = firstStart.SessionId,
                ClientId = "guest-test",
                Uci = "e7e5"
            },
            userId: null);

        Assert.Equal(
            completed.Score,
            replayedCompletion.Score);

        Assert.Equal(
            1,
            await context.PuzzleAttempts.CountAsync());
    }

    [PostgresFact]
    public async Task Registered_completion_creates_score_outbox_and_bonus_assignment()
    {
        await using var database =
            await PostgresTestDatabase.CreateAsync();

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

        var dailyPuzzle =
            CreatePuzzle("daily-registered", rating: 1200);

        var bonusPuzzle =
            CreatePuzzle("bonus-candidate", rating: 2600);

        await using (var seedContext = database.CreateContext())
        {
            seedContext.Users.Add(user);

            seedContext.Puzzles.AddRange(
                dailyPuzzle,
                bonusPuzzle);

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

        var session =
            await service.StartSessionAsync(user.Id, null);

        var result = await service.SubmitMoveAsync(
            new PuzzleMoveRequest
            {
                SessionId = session.SessionId,
                Uci = "e7e5"
            },
            user.Id);

        Assert.True(result.IsCompleted);
        Assert.NotNull(result.Score);

        Assert.Equal(
            1,
            await context.PuzzleAttempts.CountAsync());

        Assert.Equal(
            1,
            await context.LeaderboardCacheOutboxes.CountAsync());

        Assert.Equal(
            4,
            await context.LeaderboardCacheOutboxDeliveries.CountAsync());

        Assert.Equal(
            1,
            await context.BonusPuzzleAssignments.CountAsync());
    }

    [PostgresFact]
    public async Task Concurrent_registered_session_starts_create_only_one_session()
    {
        await using var database =
            await PostgresTestDatabase.CreateAsync();

        await database.SeedDailyPuzzleAsync(
            PuzzleAudience.Registered);

        var user = new User
        {
            Username = "concurrent-user",
            FirstName = "Concurrent",
            LastName = "Test",
            Email = "concurrent@example.test",
            EmailVerified = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        await using (var seedContext = database.CreateContext())
        {
            seedContext.Users.Add(user);
            await seedContext.SaveChangesAsync();
        }

        await using var firstContext =
            database.CreateContext();

        await using var secondContext =
            database.CreateContext();

        var firstService = CreateService(firstContext);
        var secondService = CreateService(secondContext);

        var startGate = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var firstTask = Task.Run(async () =>
        {
            await startGate.Task;

            return await firstService.StartSessionAsync(
                user.Id,
                null);
        });

        var secondTask = Task.Run(async () =>
        {
            await startGate.Task;

            return await secondService.StartSessionAsync(
                user.Id,
                null);
        });

        startGate.SetResult(true);

        var results = await Task
            .WhenAll(firstTask, secondTask)
            .WaitAsync(TimeSpan.FromSeconds(30));

        Assert.NotNull(results[0].SessionId);
        Assert.NotNull(results[1].SessionId);

        Assert.Equal(
            results[0].SessionId,
            results[1].SessionId);

        await using var verificationContext =
            database.CreateContext();

        var sessions = await verificationContext
            .PuzzleGameSessions
            .AsNoTracking()
            .Where(x => x.UserId == user.Id)
            .ToListAsync();

        var session = Assert.Single(sessions);

        Assert.Equal(
            results[0].SessionId!.Value,
            session.Id);

        Assert.Equal(
            0,
            await verificationContext.PuzzleAttempts.CountAsync());

        Assert.Equal(
            0,
            await verificationContext.LeaderboardCacheOutboxes.CountAsync());

        Assert.Equal(
            0,
            await verificationContext.LeaderboardCacheOutboxDeliveries.CountAsync());

        Assert.Equal(
            0,
            await verificationContext.BonusPuzzleAssignments.CountAsync());
    }

    [PostgresFact]
    public async Task Concurrent_registered_completions_create_only_one_attempt()
    {
        await using var database =
            await PostgresTestDatabase.CreateAsync();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var user = new User
        {
            Username = "concurrent-completion-user",
            FirstName = "Concurrent",
            LastName = "Completion",
            Email = "completion@example.test",
            EmailVerified = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        var dailyPuzzle =
            CreatePuzzle("concurrent-daily", rating: 1200);

        var bonusPuzzle =
            CreatePuzzle("concurrent-bonus", rating: 2600);

        await using (var seedContext = database.CreateContext())
        {
            seedContext.Users.Add(user);

            seedContext.Puzzles.AddRange(
                dailyPuzzle,
                bonusPuzzle);

            seedContext.DailyPuzzles.Add(new DailyPuzzle
            {
                Puzzle = dailyPuzzle,
                PuzzleDate = today,
                Audience = PuzzleAudience.Registered
            });

            await seedContext.SaveChangesAsync();
        }

        int sessionId;

        await using (var startContext = database.CreateContext())
        {
            var startService = CreateService(startContext);

            var session = await startService.StartSessionAsync(
                user.Id,
                null);

            Assert.NotNull(session.SessionId);

            sessionId = session.SessionId!.Value;
        }

        await using var firstContext =
            database.CreateContext();

        await using var secondContext =
            database.CreateContext();

        var firstService = CreateService(firstContext);
        var secondService = CreateService(secondContext);

        var startGate = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var firstTask = Task.Run(async () =>
        {
            await startGate.Task;

            return await firstService.SubmitMoveAsync(
                new PuzzleMoveRequest
                {
                    SessionId = sessionId,
                    Uci = "e7e5"
                },
                user.Id);
        });

        var secondTask = Task.Run(async () =>
        {
            await startGate.Task;

            return await secondService.SubmitMoveAsync(
                new PuzzleMoveRequest
                {
                    SessionId = sessionId,
                    Uci = "e7e5"
                },
                user.Id);
        });

        startGate.SetResult(true);

        var results = await Task
            .WhenAll(firstTask, secondTask)
            .WaitAsync(TimeSpan.FromSeconds(30));

        Assert.All(results, result =>
        {
            Assert.True(result.IsCompleted);
            Assert.NotNull(result.Score);
        });

        Assert.Equal(
            results[0].Score,
            results[1].Score);

        await using var verificationContext =
            database.CreateContext();

        Assert.Equal(
            1,
            await verificationContext.PuzzleAttempts.CountAsync());

        Assert.Equal(
            1,
            await verificationContext.LeaderboardCacheOutboxes.CountAsync());

        Assert.Equal(
            4,
            await verificationContext.LeaderboardCacheOutboxDeliveries.CountAsync());

        Assert.Equal(
            1,
            await verificationContext.BonusPuzzleAssignments.CountAsync());

        Assert.Equal(
            1,
            await verificationContext.PuzzleGameSessions.CountAsync());
    }

    [PostgresFact]
    public async Task Concurrent_correct_moves_do_not_penalize_duplicate_request()
    {
        await using var database =
            await PostgresTestDatabase.CreateAsync();

        var userId = await SeedConcurrencyScenarioAsync(
            database,
            "e2e4 e7e5 g1f3 b8c6");

        var sessionId = await StartRegisteredSessionAsync(
            database,
            userId);

        await RunConcurrentlyAsync(
            database,
            service => service.SubmitMoveAsync(
                new PuzzleMoveRequest
                {
                    SessionId = sessionId,
                    Uci = "e7e5",
                    ExpectedMoveIndex = 1
                },
                userId),
            service => service.SubmitMoveAsync(
                new PuzzleMoveRequest
                {
                    SessionId = sessionId,
                    Uci = "e7e5",
                    ExpectedMoveIndex = 1
                },
                userId));

        await using var context = database.CreateContext();

        var session = await context.PuzzleGameSessions
            .AsNoTracking()
            .SingleAsync(x => x.Id == sessionId);

        Assert.False(session.IsCompleted);
        Assert.Equal(3, session.CurrentMoveIndex);
        Assert.Equal(0, session.WrongMoves);

        Assert.Empty(
            await context.PuzzleAttempts.ToListAsync());

        Assert.Empty(
            await context.LeaderboardCacheOutboxes.ToListAsync());
    }

    [PostgresFact]
    public async Task Concurrent_hint_and_move_preserve_session_state()
    {
        await using var database =
            await PostgresTestDatabase.CreateAsync();

        var userId = await SeedConcurrencyScenarioAsync(
            database,
            "e2e4 e7e5 g1f3 b8c6");

        var sessionId = await StartRegisteredSessionAsync(
            database,
            userId);

        await RunConcurrentlyAsync(
            database,
            service => service.UseHintAsync(
                sessionId,
                userId,
                null),
            service => service.SubmitMoveAsync(
                new PuzzleMoveRequest
                {
                    SessionId = sessionId,
                    Uci = "e7e5"
                },
                userId));

        await using var context = database.CreateContext();

        var session = await context.PuzzleGameSessions
            .AsNoTracking()
            .SingleAsync(x => x.Id == sessionId);

        Assert.True(session.UsedHint);
        Assert.False(session.IsCompleted);
        Assert.Equal(3, session.CurrentMoveIndex);
        Assert.Equal(0, session.WrongMoves);

        Assert.Empty(
            await context.PuzzleAttempts.ToListAsync());

        Assert.Empty(
            await context.LeaderboardCacheOutboxes.ToListAsync());
    }

    [PostgresFact]
    public async Task Concurrent_bonus_starts_preserve_single_assignment()
    {
        await using var database =
            await PostgresTestDatabase.CreateAsync();

        var userId = await SeedConcurrencyScenarioAsync(
            database);

        await CompleteRegisteredDailyPuzzleAsync(
            database,
            userId);

        var results = await RunConcurrentlyAsync(
            database,
            service => service.StartBonusAsync(userId),
            service => service.StartBonusAsync(userId));

        Assert.True(results.First.IsEligible);
        Assert.True(results.Second.IsEligible);

        await using var context = database.CreateContext();

        var assignment = Assert.Single(
            await context.BonusPuzzleAssignments
                .AsNoTracking()
                .Where(x => x.UserId == userId)
                .ToListAsync());

        Assert.NotNull(assignment.StartedAtUtc);
        Assert.False(assignment.IsCompleted);
        Assert.Null(assignment.PuzzleAttemptId);

        var originalStartedAt = assignment.StartedAtUtc;

        await using (var restartContext = database.CreateContext())
        {
            var service = CreateService(restartContext);

            var restarted = await service.StartBonusAsync(userId);

            Assert.True(restarted.IsEligible);
        }

        var reloadedAssignment =
            await context.BonusPuzzleAssignments
                .AsNoTracking()
                .SingleAsync(x => x.Id == assignment.Id);

        Assert.Equal(
            originalStartedAt,
            reloadedAssignment.StartedAtUtc);

        Assert.Equal(
            1,
            await context.PuzzleAttempts.CountAsync());

        Assert.Equal(
            1,
            await context.LeaderboardCacheOutboxes.CountAsync());

        Assert.Equal(
            4,
            await context.LeaderboardCacheOutboxDeliveries.CountAsync());
    }

    [PostgresFact]
    public async Task Concurrent_bonus_completions_create_only_one_bonus_attempt()
    {
        await using var database =
            await PostgresTestDatabase.CreateAsync();

        var userId = await SeedConcurrencyScenarioAsync(
            database);

        await CompleteRegisteredDailyPuzzleAsync(
            database,
            userId);

        await using (var startContext = database.CreateContext())
        {
            var service = CreateService(startContext);

            var bonus = await service.StartBonusAsync(userId);

            Assert.True(bonus.IsEligible);

            var moveResult = await service.SubmitBonusMoveAsync(
                new PuzzleMoveRequest
                {
                    Uci = "e7e5"
                },
                userId);

            Assert.True(moveResult.IsSolved);
            Assert.False(moveResult.IsCompleted);
        }

        var results = await RunConcurrentlyAsync(
            database,
            service => service.SubmitBonusAsync(userId),
            service => service.SubmitBonusAsync(userId));

        Assert.True(results.First.IsCompleted);
        Assert.True(results.Second.IsCompleted);

        Assert.Equal(
            results.First.Score,
            results.Second.Score);

        Assert.Equal(
            results.First.Seconds,
            results.Second.Seconds);

        await using var context = database.CreateContext();

        var assignment = Assert.Single(
            await context.BonusPuzzleAssignments
                .AsNoTracking()
                .Where(x => x.UserId == userId)
                .ToListAsync());

        Assert.True(assignment.IsCompleted);
        Assert.NotNull(assignment.PuzzleAttemptId);

        var attempts = await context.PuzzleAttempts
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .ToListAsync();

        Assert.Equal(2, attempts.Count);

        var bonusAttempt = Assert.Single(
            attempts,
            x => x.Id == assignment.PuzzleAttemptId);

        Assert.Equal(
            results.First.Score,
            bonusAttempt.Score);

        Assert.Equal(
            assignment.PuzzleId,
            bonusAttempt.PuzzleId);

        Assert.Equal(
            2,
            await context.LeaderboardCacheOutboxes.CountAsync());

        Assert.Equal(
            8,
            await context.LeaderboardCacheOutboxDeliveries.CountAsync());
    }

    [PostgresFact]
    public async Task Future_move_index_is_rejected_without_changing_session()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var userId = await SeedConcurrencyScenarioAsync(database, "e2e4 e7e5 g1f3 b8c6");
        var sessionId = await StartRegisteredSessionAsync(database, userId);
        await using (var context = database.CreateContext())
        {
            var service = CreateService(context);
            var error = await Assert.ThrowsAsync<AppException>(() => service.SubmitMoveAsync(
                new PuzzleMoveRequest { SessionId = sessionId, Uci = "b8c6", ExpectedMoveIndex = 3 }, userId));
            Assert.Equal(409, error.StatusCode);
            Assert.Equal("INVALID_MOVE_INDEX", error.ErrorCode);
        }
        await using var verificationContext = database.CreateContext();
        var session = await verificationContext.PuzzleGameSessions.AsNoTracking().SingleAsync(x => x.Id == sessionId);
        Assert.Equal(1, session.CurrentMoveIndex);
        Assert.Equal(0, session.WrongMoves);
        Assert.False(session.IsCompleted);
        Assert.Empty(await verificationContext.PuzzleAttempts.ToListAsync());
    }

    [PostgresFact]
    public async Task Stale_move_index_returns_current_state_without_penalty()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var userId = await SeedConcurrencyScenarioAsync(database, "e2e4 e7e5 g1f3 b8c6");
        var sessionId = await StartRegisteredSessionAsync(database, userId);
        await using (var context = database.CreateContext())
        {
            var first = await CreateService(context).SubmitMoveAsync(
                new PuzzleMoveRequest { SessionId = sessionId, Uci = "e7e5", ExpectedMoveIndex = 1 }, userId);
            Assert.Equal(3, first.CurrentMoveIndex);
        }
        await using (var context = database.CreateContext())
        {
            var stale = await CreateService(context).SubmitMoveAsync(
                new PuzzleMoveRequest { SessionId = sessionId, Uci = "b8c6", ExpectedMoveIndex = 1 }, userId);
            Assert.Equal(3, stale.CurrentMoveIndex);
            Assert.Equal(0, stale.WrongMoves);
            Assert.False(stale.IsCompleted);
        }
        await using var verificationContext = database.CreateContext();
        var session = await verificationContext.PuzzleGameSessions.AsNoTracking().SingleAsync(x => x.Id == sessionId);
        Assert.Equal(3, session.CurrentMoveIndex);
        Assert.Equal(0, session.WrongMoves);
        Assert.False(session.IsCompleted);
        Assert.Empty(await verificationContext.PuzzleAttempts.ToListAsync());
    }

    [PostgresFact]
    public async Task Wrong_move_at_current_index_is_penalized()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var userId = await SeedConcurrencyScenarioAsync(database, "e2e4 e7e5 g1f3 b8c6");
        var sessionId = await StartRegisteredSessionAsync(database, userId);
        await using var context = database.CreateContext();
        var result = await CreateService(context).SubmitMoveAsync(
            new PuzzleMoveRequest { SessionId = sessionId, Uci = "d7d5", ExpectedMoveIndex = 1 }, userId);
        Assert.Equal(1, result.CurrentMoveIndex);
        Assert.Equal(1, result.WrongMoves);
        Assert.False(result.IsCompleted);
        await using var verificationContext = database.CreateContext();
        var session = await verificationContext.PuzzleGameSessions.AsNoTracking().SingleAsync(x => x.Id == sessionId);
        Assert.Equal(1, session.CurrentMoveIndex);
        Assert.Equal(1, session.WrongMoves);
        Assert.False(session.IsCompleted);
    }

    [PostgresFact]
    public async Task Concurrent_bonus_moves_do_not_penalize_duplicate_request()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var userId = await SeedConcurrencyScenarioAsync(database);
        await using (var seedContext = database.CreateContext())
        {
            var bonusPuzzle = await seedContext.Puzzles.SingleAsync(x => x.LichessPuzzleId == "concurrency-bonus");
            bonusPuzzle.Moves = "e2e4 e7e5 g1f3 b8c6";
            await seedContext.SaveChangesAsync();
        }
        await CompleteRegisteredDailyPuzzleAsync(database, userId);
        await using (var startContext = database.CreateContext())
        {
            var bonus = await CreateService(startContext).StartBonusAsync(userId);
            Assert.True(bonus.IsEligible);
        }
        var results = await RunConcurrentlyAsync(database,
            service => service.SubmitBonusMoveAsync(new PuzzleMoveRequest { Uci = "e7e5", ExpectedMoveIndex = 1 }, userId),
            service => service.SubmitBonusMoveAsync(new PuzzleMoveRequest { Uci = "e7e5", ExpectedMoveIndex = 1 }, userId));
        Assert.Equal(3, results.First.CurrentMoveIndex);
        Assert.Equal(3, results.Second.CurrentMoveIndex);
        Assert.Equal(0, results.First.WrongMoves);
        Assert.Equal(0, results.Second.WrongMoves);
        await using var context = database.CreateContext();
        var assignment = await context.BonusPuzzleAssignments.AsNoTracking().SingleAsync(x => x.UserId == userId);
        Assert.Equal(3, assignment.CurrentMoveIndex);
        Assert.Equal(0, assignment.WrongMoves);
        Assert.False(assignment.IsCompleted);
        Assert.Null(assignment.CompletedAtUtc);
        Assert.Null(assignment.PuzzleAttemptId);
        Assert.Equal(1, await context.PuzzleAttempts.CountAsync());
    }

    [PostgresFact]
    public async Task Repeated_final_bonus_move_preserves_completion_time()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var userId = await SeedConcurrencyScenarioAsync(database);
        await CompleteRegisteredDailyPuzzleAsync(database, userId);
        await using (var context = database.CreateContext())
        {
            var service = CreateService(context);
            await service.StartBonusAsync(userId);
            var result = await service.SubmitBonusMoveAsync(new PuzzleMoveRequest { Uci = "e7e5", ExpectedMoveIndex = 1 }, userId);
            Assert.True(result.IsSolved);
        }
        DateTime? originalCompletedAt;
        await using (var context = database.CreateContext())
        {
            var assignment = await context.BonusPuzzleAssignments.AsNoTracking().SingleAsync(x => x.UserId == userId);
            originalCompletedAt = assignment.CompletedAtUtc;
            Assert.NotNull(originalCompletedAt);
        }
        await using (var context = database.CreateContext())
        {
            var error = await Assert.ThrowsAsync<AppException>(() => CreateService(context).SubmitBonusMoveAsync(
                new PuzzleMoveRequest { Uci = "e7e5", ExpectedMoveIndex = 1 }, userId));
            Assert.Equal(409, error.StatusCode);
            Assert.Equal("BONUS_ALREADY_COMPLETED", error.ErrorCode);
        }
        await using var verificationContext = database.CreateContext();
        var finalAssignment = await verificationContext.BonusPuzzleAssignments.AsNoTracking().SingleAsync(x => x.UserId == userId);
        Assert.Equal(originalCompletedAt, finalAssignment.CompletedAtUtc);
        Assert.Equal(0, finalAssignment.WrongMoves);
        Assert.Equal(2, finalAssignment.CurrentMoveIndex);
        Assert.Null(finalAssignment.PuzzleAttemptId);
    }

    [PostgresFact]
    public async Task Repeated_bonus_submission_returns_original_score()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var userId = await SeedConcurrencyScenarioAsync(database);
        await CompleteRegisteredDailyPuzzleAsync(database, userId);
        decimal originalScore;
        int originalSeconds;
        await using (var context = database.CreateContext())
        {
            var service = CreateService(context);
            await service.StartBonusAsync(userId);
            var moveResult = await service.SubmitBonusMoveAsync(new PuzzleMoveRequest { Uci = "e7e5", ExpectedMoveIndex = 1 }, userId);
            Assert.True(moveResult.IsSolved);
            var result = await service.SubmitBonusAsync(userId);
            Assert.True(result.IsCompleted);
            originalScore = result.Score;
            originalSeconds = result.Seconds;
        }
        await using (var context = database.CreateContext())
        {
            var repeated = await CreateService(context).SubmitBonusAsync(userId);
            Assert.True(repeated.IsCompleted);
            Assert.Equal(originalScore, repeated.Score);
            Assert.Equal(originalSeconds, repeated.Seconds);
        }
        await using var verificationContext = database.CreateContext();
        var assignment = await verificationContext.BonusPuzzleAssignments.AsNoTracking().SingleAsync(x => x.UserId == userId);
        Assert.True(assignment.IsCompleted);
        Assert.NotNull(assignment.PuzzleAttemptId);
        var bonusAttempt = await verificationContext.PuzzleAttempts.AsNoTracking().SingleAsync(x => x.Id == assignment.PuzzleAttemptId);
        Assert.Equal(originalScore, bonusAttempt.Score);
        Assert.Equal(originalSeconds, bonusAttempt.Seconds);
        Assert.Equal(2, await verificationContext.PuzzleAttempts.CountAsync());
        Assert.Equal(2, await verificationContext.LeaderboardCacheOutboxes.CountAsync());
        Assert.Equal(8, await verificationContext.LeaderboardCacheOutboxDeliveries.CountAsync());
    }

    [PostgresFact]
    public async Task Daily_session_preserves_country_after_country_change()
    {
        await using var database =
            await PostgresTestDatabase.CreateAsync();

        var userId = await SeedConcurrencyScenarioAsync(database);

        await SetTestCountryAsync(database, userId, "TR");

        var sessionId = await StartRegisteredSessionAsync(
            database,
            userId);

        await using (var context = database.CreateContext())
        {
            var session = await context.PuzzleGameSessions
                .AsNoTracking()
                .SingleAsync(x => x.Id == sessionId);

            Assert.Equal("TR", session.CountryCode);
        }

        await SetTestCountryAsync(database, userId, "DE");

        var resumedSessionId = await StartRegisteredSessionAsync(
            database,
            userId);

        Assert.Equal(sessionId, resumedSessionId);

        await using (var context = database.CreateContext())
        {
            var service = CreateService(context);

            var result = await service.SubmitMoveAsync(
                new PuzzleMoveRequest
                {
                    SessionId = sessionId,
                    Uci = "e7e5"
                },
                userId);

            Assert.True(result.IsCompleted);
            Assert.NotNull(result.Score);
        }

        await using var verificationContext =
            database.CreateContext();

        var user = await verificationContext.Users
            .AsNoTracking()
            .SingleAsync(x => x.Id == userId);

        Assert.Equal("DE", user.CountryCode);

        var completedSession =
            await verificationContext.PuzzleGameSessions
                .AsNoTracking()
                .SingleAsync(x => x.Id == sessionId);

        Assert.True(completedSession.IsCompleted);
        Assert.Equal("TR", completedSession.CountryCode);

        var attempt = await verificationContext.PuzzleAttempts
            .AsNoTracking()
            .SingleAsync();

        Assert.Equal(userId, attempt.UserId);
        Assert.Equal("TR", attempt.CountryCode);

        var deliveries =
            await verificationContext.LeaderboardCacheOutboxDeliveries
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
    public async Task Daily_session_without_country_remains_global_only()
    {
        await using var database =
            await PostgresTestDatabase.CreateAsync();

        var userId = await SeedConcurrencyScenarioAsync(database);

        var sessionId = await StartRegisteredSessionAsync(
            database,
            userId);

        await using (var context = database.CreateContext())
        {
            var session = await context.PuzzleGameSessions
                .AsNoTracking()
                .SingleAsync(x => x.Id == sessionId);

            Assert.Null(session.CountryCode);
        }

        await SetTestCountryAsync(database, userId, "TR");

        var resumedSessionId = await StartRegisteredSessionAsync(
            database,
            userId);

        Assert.Equal(sessionId, resumedSessionId);

        await using (var context = database.CreateContext())
        {
            var result = await CreateService(context)
                .SubmitMoveAsync(
                    new PuzzleMoveRequest
                    {
                        SessionId = sessionId,
                        Uci = "e7e5"
                    },
                    userId);

            Assert.True(result.IsCompleted);
        }

        await using var verificationContext =
            database.CreateContext();

        var user = await verificationContext.Users
            .AsNoTracking()
            .SingleAsync(x => x.Id == userId);

        Assert.Equal("TR", user.CountryCode);

        var sessionAfterCompletion =
            await verificationContext.PuzzleGameSessions
                .AsNoTracking()
                .SingleAsync(x => x.Id == sessionId);

        Assert.Null(sessionAfterCompletion.CountryCode);

        var attempt = await verificationContext.PuzzleAttempts
            .AsNoTracking()
            .SingleAsync();

        Assert.Equal(userId, attempt.UserId);
        Assert.Null(attempt.CountryCode);

        var deliveries =
            await verificationContext.LeaderboardCacheOutboxDeliveries
                .AsNoTracking()
                .ToListAsync();

        Assert.Equal(4, deliveries.Count);

        Assert.All(deliveries, delivery =>
        {
            Assert.Null(delivery.CountryCode);
            Assert.Equal(attempt.Score, delivery.TotalScore);
            Assert.Equal(1, delivery.SolvedCount);
        });
    }

    [PostgresFact]
    public async Task Guest_session_has_no_country_or_leaderboard_outbox()
    {
        await using var database =
            await PostgresTestDatabase.CreateAsync();

        await database.SeedDailyPuzzleAsync(
            PuzzleAudience.Guest);

        const string clientId = "guest-country-test";

        await using (var context = database.CreateContext())
        {
            var service = CreateService(context);

            var session = await service.StartSessionAsync(
                null,
                clientId);

            Assert.NotNull(session.SessionId);

            var result = await service.SubmitMoveAsync(
                new PuzzleMoveRequest
                {
                    SessionId = session.SessionId,
                    ClientId = clientId,
                    Uci = "e7e5"
                },
                userId: null);

            Assert.True(result.IsCompleted);
        }

        await using var verificationContext =
            database.CreateContext();

        var savedSession =
            await verificationContext.PuzzleGameSessions
                .AsNoTracking()
                .SingleAsync();

        Assert.Null(savedSession.UserId);
        Assert.Null(savedSession.CountryCode);
        Assert.Equal(clientId, savedSession.ClientId);

        var attempt = await verificationContext.PuzzleAttempts
            .AsNoTracking()
            .SingleAsync();

        Assert.Null(attempt.UserId);
        Assert.Null(attempt.CountryCode);

        Assert.Equal(
            0,
            await verificationContext.LeaderboardCacheOutboxes
                .CountAsync());

        Assert.Equal(
            0,
            await verificationContext.LeaderboardCacheOutboxDeliveries
                .CountAsync());
    }

    [PostgresFact]
    public async Task Bonus_country_is_captured_at_start_not_assignment()
    {
        await using var database =
            await PostgresTestDatabase.CreateAsync();

        var userId =
            await SeedConcurrencyScenarioAsync(database);

        await SetTestCountryAsync(database, userId, "TR");

        await CompleteRegisteredDailyPuzzleAsync(
            database,
            userId);

        await using (var context = database.CreateContext())
        {
            var assignment = await context.BonusPuzzleAssignments
                .AsNoTracking()
                .SingleAsync(x => x.UserId == userId);

            Assert.Null(assignment.CountryCode);
            Assert.Null(assignment.StartedAtUtc);
        }

        await SetTestCountryAsync(database, userId, "DE");

        await using (var context = database.CreateContext())
        {
            await CreateService(context)
                .StartBonusAsync(userId);
        }

        await using var verificationContext =
            database.CreateContext();

        var startedAssignment =
            await verificationContext.BonusPuzzleAssignments
                .AsNoTracking()
                .SingleAsync(x => x.UserId == userId);

        Assert.Equal("DE", startedAssignment.CountryCode);
        Assert.NotNull(startedAssignment.StartedAtUtc);
        Assert.False(startedAssignment.IsCompleted);

        var dailyAttempt =
            await verificationContext.PuzzleAttempts
                .AsNoTracking()
                .SingleAsync();

        Assert.Equal("TR", dailyAttempt.CountryCode);
    }

    [PostgresFact]
    public async Task Bonus_session_preserves_country_after_country_change()
    {
        await using var database =
            await PostgresTestDatabase.CreateAsync();

        var userId =
            await SeedConcurrencyScenarioAsync(database);

        await SetTestCountryAsync(database, userId, "TR");

        await CompleteRegisteredDailyPuzzleAsync(
            database,
            userId);

        await using (var context = database.CreateContext())
        {
            await CreateService(context)
                .StartBonusAsync(userId);
        }

        DateTime? originalStartedAt;

        await using (var context = database.CreateContext())
        {
            var assignment = await context.BonusPuzzleAssignments
                .AsNoTracking()
                .SingleAsync(x => x.UserId == userId);

            Assert.Equal("TR", assignment.CountryCode);

            originalStartedAt = assignment.StartedAtUtc;

            Assert.NotNull(originalStartedAt);
        }

        await SetTestCountryAsync(database, userId, "DE");

        await using (var context = database.CreateContext())
        {
            await CreateService(context)
                .StartBonusAsync(userId);
        }

        await using (var context = database.CreateContext())
        {
            var assignment = await context.BonusPuzzleAssignments
                .AsNoTracking()
                .SingleAsync(x => x.UserId == userId);

            Assert.Equal("TR", assignment.CountryCode);

            Assert.Equal(
                originalStartedAt,
                assignment.StartedAtUtc);
        }

        decimal bonusScore;

        await using (var context = database.CreateContext())
        {
            var service = CreateService(context);

            var moveResult = await service.SubmitBonusMoveAsync(
                new PuzzleMoveRequest
                {
                    Uci = "e7e5",
                    ExpectedMoveIndex = 1
                },
                userId);

            Assert.True(moveResult.IsSolved);

            var result = await service.SubmitBonusAsync(userId);

            Assert.True(result.IsCompleted);

            bonusScore = result.Score;
        }

        await using var verificationContext =
            database.CreateContext();

        var user = await verificationContext.Users
            .AsNoTracking()
            .SingleAsync(x => x.Id == userId);

        Assert.Equal("DE", user.CountryCode);

        var completedAssignment =
            await verificationContext.BonusPuzzleAssignments
                .AsNoTracking()
                .SingleAsync(x => x.UserId == userId);

        Assert.True(completedAssignment.IsCompleted);
        Assert.Equal("TR", completedAssignment.CountryCode);
        Assert.NotNull(completedAssignment.PuzzleAttemptId);

        var attempts = await verificationContext.PuzzleAttempts
            .AsNoTracking()
            .ToListAsync();

        Assert.Equal(2, attempts.Count);

        Assert.All(
            attempts,
            attempt => Assert.Equal("TR", attempt.CountryCode));

        var bonusAttempt = Assert.Single(
            attempts,
            x => x.Id == completedAssignment.PuzzleAttemptId);

        Assert.Equal(bonusScore, bonusAttempt.Score);

        var deliveries =
            await verificationContext.LeaderboardCacheOutboxDeliveries
                .AsNoTracking()
                .ToListAsync();

        Assert.Equal(16, deliveries.Count);

        Assert.Equal(
            8,
            deliveries.Count(x => x.CountryCode == null));

        Assert.Equal(
            8,
            deliveries.Count(x => x.CountryCode == "TR"));

        Assert.DoesNotContain(
            deliveries,
            x => x.CountryCode == "DE");
    }

    [PostgresFact]
    public async Task Bonus_session_without_country_remains_global_only()
    {
        await using var database =
            await PostgresTestDatabase.CreateAsync();

        var userId =
            await SeedConcurrencyScenarioAsync(database);

        await CompleteRegisteredDailyPuzzleAsync(
            database,
            userId);

        await using (var context = database.CreateContext())
        {
            await CreateService(context)
                .StartBonusAsync(userId);
        }

        await using (var context = database.CreateContext())
        {
            var assignment = await context.BonusPuzzleAssignments
                .AsNoTracking()
                .SingleAsync(x => x.UserId == userId);

            Assert.Null(assignment.CountryCode);
            Assert.NotNull(assignment.StartedAtUtc);
        }

        await SetTestCountryAsync(database, userId, "TR");

        await using (var context = database.CreateContext())
        {
            var service = CreateService(context);

            await service.StartBonusAsync(userId);

            var moveResult = await service.SubmitBonusMoveAsync(
                new PuzzleMoveRequest
                {
                    Uci = "e7e5",
                    ExpectedMoveIndex = 1
                },
                userId);

            Assert.True(moveResult.IsSolved);

            var result = await service.SubmitBonusAsync(userId);

            Assert.True(result.IsCompleted);
        }

        await using var verificationContext =
            database.CreateContext();

        var user = await verificationContext.Users
            .AsNoTracking()
            .SingleAsync(x => x.Id == userId);

        Assert.Equal("TR", user.CountryCode);

        var assignmentAfterCompletion =
            await verificationContext.BonusPuzzleAssignments
                .AsNoTracking()
                .SingleAsync(x => x.UserId == userId);

        Assert.True(assignmentAfterCompletion.IsCompleted);
        Assert.Null(assignmentAfterCompletion.CountryCode);
        Assert.NotNull(assignmentAfterCompletion.PuzzleAttemptId);

        var attempts = await verificationContext.PuzzleAttempts
            .AsNoTracking()
            .ToListAsync();

        Assert.Equal(2, attempts.Count);

        Assert.All(
            attempts,
            attempt => Assert.Null(attempt.CountryCode));

        var deliveries =
            await verificationContext.LeaderboardCacheOutboxDeliveries
                .AsNoTracking()
                .ToListAsync();

        Assert.Equal(8, deliveries.Count);

        Assert.All(
            deliveries,
            delivery => Assert.Null(delivery.CountryCode));
    }

    [PostgresFact]
    public async Task Daily_and_bonus_scores_remain_in_separate_countries()
    {
        await using var database =
            await PostgresTestDatabase.CreateAsync();

        var userId =
            await SeedConcurrencyScenarioAsync(database);

        await SetTestCountryAsync(database, userId, "TR");

        await CompleteRegisteredDailyPuzzleAsync(
            database,
            userId);

        await SetTestCountryAsync(database, userId, "DE");

        decimal originalBonusScore;

        await using (var context = database.CreateContext())
        {
            var service = CreateService(context);

            await service.StartBonusAsync(userId);

            var moveResult = await service.SubmitBonusMoveAsync(
                new PuzzleMoveRequest
                {
                    Uci = "e7e5",
                    ExpectedMoveIndex = 1
                },
                userId);

            Assert.True(moveResult.IsSolved);

            var result = await service.SubmitBonusAsync(userId);

            Assert.True(result.IsCompleted);

            originalBonusScore = result.Score;
        }

        await SetTestCountryAsync(database, userId, "GB");

        await using (var context = database.CreateContext())
        {
            var repeated = await CreateService(context)
                .SubmitBonusAsync(userId);

            Assert.True(repeated.IsCompleted);

            Assert.Equal(
                originalBonusScore,
                repeated.Score);
        }

        await using var verificationContext =
            database.CreateContext();

        var attempts = await verificationContext.PuzzleAttempts
            .AsNoTracking()
            .ToListAsync();

        Assert.Equal(2, attempts.Count);

        var dailyAttempt = Assert.Single(
            attempts,
            x => x.CountryCode == "TR");

        var bonusAttempt = Assert.Single(
            attempts,
            x => x.CountryCode == "DE");

        Assert.Equal(originalBonusScore, bonusAttempt.Score);

        Assert.DoesNotContain(
            attempts,
            x => x.CountryCode == "GB");

        var assignment =
            await verificationContext.BonusPuzzleAssignments
                .AsNoTracking()
                .SingleAsync(x => x.UserId == userId);

        Assert.Equal("DE", assignment.CountryCode);
        Assert.Equal(bonusAttempt.Id, assignment.PuzzleAttemptId);

        Assert.Equal(
            2,
            await verificationContext.LeaderboardCacheOutboxes
                .CountAsync());

        var deliveries =
            await verificationContext.LeaderboardCacheOutboxDeliveries
                .AsNoTracking()
                .ToListAsync();

        Assert.Equal(16, deliveries.Count);

        var turkeyDeliveries = deliveries
            .Where(x => x.CountryCode == "TR")
            .ToList();

        var germanyDeliveries = deliveries
            .Where(x => x.CountryCode == "DE")
            .ToList();

        var globalDeliveries = deliveries
            .Where(x => x.CountryCode == null)
            .ToList();

        Assert.Equal(4, turkeyDeliveries.Count);
        Assert.Equal(4, germanyDeliveries.Count);
        Assert.Equal(8, globalDeliveries.Count);

        Assert.All(turkeyDeliveries, delivery =>
        {
            Assert.Equal(dailyAttempt.Score, delivery.TotalScore);
            Assert.Equal(1, delivery.SolvedCount);
        });

        Assert.All(germanyDeliveries, delivery =>
        {
            Assert.Equal(bonusAttempt.Score, delivery.TotalScore);
            Assert.Equal(1, delivery.SolvedCount);
        });

        var totalScore =
            dailyAttempt.Score + bonusAttempt.Score;

        var latestGlobalDeliveries = globalDeliveries
            .Where(x => x.SolvedCount == 2)
            .ToList();

        Assert.Equal(4, latestGlobalDeliveries.Count);

        Assert.All(latestGlobalDeliveries, delivery =>
        {
            Assert.Equal(totalScore, delivery.TotalScore);
        });

        Assert.DoesNotContain(
            deliveries,
            x => x.CountryCode == "GB");
    }

    // ---------------------------------------------------------
    // CONCURRENCY TEST HELPERS
    // ---------------------------------------------------------

    private static async Task SetTestCountryAsync(
    PostgresTestDatabase database,
    int userId,
    string? countryCode)
    {
        await using var context = database.CreateContext();

        var user = await context.Users
            .SingleAsync(x => x.Id == userId);

        var now = DateTime.UtcNow;

        user.CountryCode = countryCode;
        user.CountryChangedAtUtc = countryCode == null
            ? null
            : now;

        user.UpdatedAtUtc = now;

        await context.SaveChangesAsync();
    }

    private static async Task<int> SeedConcurrencyScenarioAsync(
        PostgresTestDatabase database,
        string dailyMoves = "e2e4 e7e5")
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var user = new User
        {
            Username = "concurrency-test-user",
            FirstName = "Concurrency",
            LastName = "Test",
            Email = "concurrency@example.test",
            EmailVerified = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        var dailyPuzzle = CreatePuzzle(
            "concurrency-daily",
            rating: 1200);

        dailyPuzzle.Moves = dailyMoves;

        var bonusPuzzle = CreatePuzzle(
            "concurrency-bonus",
            rating: 2600);

        await using var context = database.CreateContext();

        context.Users.Add(user);

        context.Puzzles.AddRange(
            dailyPuzzle,
            bonusPuzzle);

        context.DailyPuzzles.Add(new DailyPuzzle
        {
            Puzzle = dailyPuzzle,
            PuzzleDate = today,
            Audience = PuzzleAudience.Registered
        });

        await context.SaveChangesAsync();

        return user.Id;
    }

    private static async Task<int> StartRegisteredSessionAsync(
        PostgresTestDatabase database,
        int userId)
    {
        await using var context = database.CreateContext();

        var service = CreateService(context);

        var session = await service.StartSessionAsync(
            userId,
            null);

        Assert.NotNull(session.SessionId);

        return session.SessionId!.Value;
    }

    private static async Task CompleteRegisteredDailyPuzzleAsync(
        PostgresTestDatabase database,
        int userId)
    {
        var sessionId = await StartRegisteredSessionAsync(
            database,
            userId);

        await using var context = database.CreateContext();

        var service = CreateService(context);

        var result = await service.SubmitMoveAsync(
            new PuzzleMoveRequest
            {
                SessionId = sessionId,
                Uci = "e7e5"
            },
            userId);

        Assert.True(result.IsCompleted);
        Assert.Equal(0, result.WrongMoves);
        Assert.False(result.UsedHint);
    }

    private static async Task<(T First, T Second)>
        RunConcurrentlyAsync<T>(
            PostgresTestDatabase database,
            Func<PuzzleService, Task<T>> firstOperation,
            Func<PuzzleService, Task<T>> secondOperation)
    {
        await using var firstContext =
            database.CreateContext();

        await using var secondContext =
            database.CreateContext();

        var firstService = CreateService(firstContext);
        var secondService = CreateService(secondContext);

        var startGate = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var firstTask = Task.Run(async () =>
        {
            await startGate.Task;

            return await firstOperation(firstService);
        });

        var secondTask = Task.Run(async () =>
        {
            await startGate.Task;

            return await secondOperation(secondService);
        });

        startGate.SetResult(true);

        var results = await Task
            .WhenAll(firstTask, secondTask)
            .WaitAsync(TimeSpan.FromSeconds(30));

        return (results[0], results[1]);
    }

    private static PuzzleService CreateService(
        ApplicationDbContext context) =>
        new(
            context,
            NullLogger<PuzzleService>.Instance,
            new NoOpDailyPuzzleService());

    private static Puzzle CreatePuzzle(
        string lichessPuzzleId,
        int rating) =>
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

    private sealed class NoOpDailyPuzzleService
        : IDailyPuzzleService
    {
        public Task AssignDailyPuzzleAsync(
            DateOnly puzzleDate,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}