using System.Globalization;
using System.Diagnostics;
using DailyChessPuzzleApi.Settings;
using Microsoft.Extensions.Options;
using DailyChessPuzzleApi.Data;
using DailyChessPuzzleApi.DTOs;
using DailyChessPuzzleApi.Models;
using DailyChessPuzzleApi.Exceptions;
using DailyChessPuzzleApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DailyChessPuzzleApi.Services
{
    public class PuzzleService : IPuzzleService
    {
        private int BonusDurationSeconds => _scoringSettings.BonusTimeLimitSeconds;
        private const int BonusMinimumRating = 2500;

        private readonly ApplicationDbContext _context;
        private readonly ILogger<PuzzleService> _logger;
        private readonly IDailyPuzzleService _dailyPuzzleService;
        private readonly LeaderboardOutboxSignal? _outboxSignal;
        private readonly ScoringSettings _scoringSettings;
        private readonly IScoringService _scoringService;

        public PuzzleService(
            ApplicationDbContext context,
            ILogger<PuzzleService> logger,
            IDailyPuzzleService dailyPuzzleService,
            LeaderboardOutboxSignal? outboxSignal = null,
            IOptions<ScoringSettings>? scoringOptions = null,
            IScoringService? scoringService = null)
        {
            _context = context;
            _logger = logger;
            _dailyPuzzleService = dailyPuzzleService;
            _outboxSignal = outboxSignal;
            _scoringSettings = scoringOptions?.Value ?? new ScoringSettings();
            _scoringService = scoringService ?? new ScoringService(Options.Create(_scoringSettings));
        }

        // ---------------------------------------------------------
        // GÜNÜN BULMACASI
        // ---------------------------------------------------------

        public async Task<PuzzleTodayResponse?> GetTodayAsync(
            int? userId,
            string? clientId,
            CancellationToken cancellationToken = default)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            clientId = NormalizeClientId(clientId);

            if (!userId.HasValue && clientId == null)
                throw new ArgumentException("CLIENT_ID_REQUIRED");

            var audience = userId.HasValue
                ? PuzzleAudience.Registered
                : PuzzleAudience.Guest;

            var puzzle = await _context.DailyPuzzles
                .AsNoTracking()
                .Where(dp =>
                    dp.PuzzleDate == today &&
                    dp.Audience == audience &&
                    dp.Puzzle.IsActive)
                .Select(dp => dp.Puzzle)
                .FirstOrDefaultAsync(cancellationToken);

            if (puzzle == null)
            {
                await _dailyPuzzleService.AssignDailyPuzzleAsync(today, cancellationToken);
                puzzle = await _context.DailyPuzzles
                    .AsNoTracking()
                    .Where(dp => dp.PuzzleDate == today &&
                                 dp.Audience == audience &&
                                 dp.Puzzle.IsActive)
                    .Select(dp => dp.Puzzle)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            if (puzzle == null)
            {
                _logger.LogWarning(
                    "No active puzzle found. Date: {Date}, Audience: {Audience}",
                    today,
                    audience);

                return null;
            }

            var attemptsQuery = _context.PuzzleAttempts
                .AsNoTracking()
                .Where(a =>
                    a.PuzzleId == puzzle.Id &&
                    a.PlayedOn == today);

            if (userId.HasValue)
            {
                attemptsQuery = attemptsQuery.Where(
                    a => a.UserId == userId.Value);
            }
            else
            {
                attemptsQuery = attemptsQuery.Where(
                    a => a.UserId == null &&
                         a.ClientId == clientId);
            }

            var previousAttempt = await attemptsQuery
                .FirstOrDefaultAsync(cancellationToken);

            return new PuzzleTodayResponse
            {
                Id = puzzle.Id,
                Difficulty = GetDifficulty(puzzle.Rating),
                IsSolved = previousAttempt != null,
                PreviousScore = previousAttempt?.Score
            };
        }

        // ---------------------------------------------------------
        // GÜNLÜK BULMACA OTURUMU
        // Her çağrıda yeni bir oturum oluşturulur.
        // ---------------------------------------------------------

        public async Task<PuzzleSessionResponse> StartSessionAsync(
            int? userId,
            string? clientId,
            CancellationToken cancellationToken = default)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            clientId = NormalizeClientId(clientId);

            if (!userId.HasValue && clientId == null)
                throw new ArgumentException("CLIENT_ID_REQUIRED");

            var puzzle = await GetDailyPuzzleAsync(
                userId,
                today,
                cancellationToken);

            if (puzzle == null)
            {
                await _dailyPuzzleService.AssignDailyPuzzleAsync(today, cancellationToken);
                puzzle = await GetDailyPuzzleAsync(userId, today, cancellationToken);
            }

            if (puzzle == null)
                throw new KeyNotFoundException("Today's puzzle was not found.");

            var moves = GetMoves(puzzle.Moves);

            if (moves.Length < 2)
                throw new AppException(500, "PUZZLE_MOVES_INVALID");

            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            await EnsureLockedUserAsync(userId, cancellationToken);

            var existingSession = await FindActiveDailySessionAsync(
                userId,
                clientId,
                today,
                cancellationToken);
            if (existingSession != null)
            {
                await transaction.CommitAsync(cancellationToken);
                return ToSessionResponse(existingSession);
            }

            string? countryCode = null;

            if (userId.HasValue)
            {
                countryCode = await _context.Users
                    .AsNoTracking()
                    .Where(x => x.Id == userId.Value)
                    .Select(x => x.CountryCode)
                    .FirstOrDefaultAsync(cancellationToken);

                countryCode = NormalizeCountryCode(countryCode);
            }

            var now = DateTime.UtcNow;

            var session = new PuzzleGameSession
            {
                PuzzleId = puzzle.Id,
                Puzzle = puzzle,
                UserId = userId,
                ClientId = userId.HasValue ? null : clientId,
                CountryCode = countryCode,
                PuzzleDate = today,
                CurrentMoveIndex = 1,
                WrongMoves = 0,
                UsedHint = false,
                StartedAtUtc = now,
                CreatedAtUtc = now,
                IsCompleted = false
            };

            _context.PuzzleGameSessions.Add(session);

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (IsUniqueViolation(exception))
            {
                _context.Entry(session).State = EntityState.Detached;
                var concurrentSession = await FindActiveDailySessionAsync(
                    userId,
                    clientId,
                    today,
                    cancellationToken);
                if (concurrentSession != null)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return ToSessionResponse(concurrentSession);
                }
                throw;
            }

            await transaction.CommitAsync(cancellationToken);
            return ToSessionResponse(session);
        }

        // ---------------------------------------------------------
        // GÜNLÜK BULMACA HAMLESİ
        // ---------------------------------------------------------

        public async Task<PuzzleSessionResponse> SubmitMoveAsync(
            PuzzleMoveRequest request,
            int? userId,
            CancellationToken cancellationToken = default)
        {
            var receivedAtUtc = DateTime.UtcNow;
            var clientId = NormalizeClientId(request.ClientId);

            if (!userId.HasValue && clientId == null)
                throw new ArgumentException("CLIENT_ID_REQUIRED");

            if (!request.SessionId.HasValue ||
                request.SessionId.Value <= 0)
            {
                throw new ArgumentException("SESSION_ID_REQUIRED");
            }

            if (string.IsNullOrWhiteSpace(request.Uci))
                throw new ArgumentException("INVALID_MOVE");

            await using var transaction =
                await _context.Database.BeginTransactionAsync(
                    cancellationToken);

            try
            {
                var session = await GetLockedDailySessionAsync(
                    request.SessionId.Value,
                    userId,
                    clientId,
                    cancellationToken);

                if (session == null)
                    throw new AppException(404, "SESSION_NOT_FOUND");

                // Tamamlanmış oturum tekrar gönderilirse
                // aynı oturumun kendi puanı döndürülür.
                if (session.IsCompleted)
                {
                    var completedScore =
                        CalculateDailySessionScore(session);

                    await transaction.CommitAsync(cancellationToken);
                    await transaction.DisposeAsync();
                    await TryCreateBonusAfterDailyCommitAsync(session, cancellationToken);

                    return ToSessionResponse(
                        session,
                        score: completedScore);
                }

                var moves = GetMoves(session.Puzzle.Moves);

                if (moves.Length < 2)
                    throw new AppException(500, "PUZZLE_MOVES_INVALID");

                if (session.CurrentMoveIndex == 0)
                    session.CurrentMoveIndex = 1;

                if (session.CurrentMoveIndex >= moves.Length)
                {
                    session.IsCompleted = true;
                    RecordDailyCompletionTime(session, receivedAtUtc);

                    await _context.SaveChangesAsync(cancellationToken);

                    var (completedScore, outboxCreated) = await FinalizeDailySessionAsync(
                        session,
                        session.PuzzleDate,
                        cancellationToken);

                    await transaction.CommitAsync(cancellationToken);
                    if (outboxCreated)
                        _outboxSignal?.Notify();
                    await transaction.DisposeAsync();
                    await TryCreateBonusAfterDailyCommitAsync(session, cancellationToken);

                    return ToSessionResponse(
                        session,
                        score: completedScore);
                }

                if (request.ExpectedMoveIndex.HasValue)
                {
                    var requestedIndex = request.ExpectedMoveIndex.Value;

                    if (requestedIndex > session.CurrentMoveIndex)
                    {
                        throw new AppException(409, "INVALID_MOVE_INDEX");
                    }

                    if (requestedIndex < session.CurrentMoveIndex)
                    {
                        await transaction.CommitAsync(cancellationToken);

                        return ToSessionResponse(session);
                    }
                }

                EnsureMoveAllowed(session.NextMoveAllowedAtUtc, receivedAtUtc);
                var processingStarted = Stopwatch.GetTimestamp();
                var expectedMove = moves[session.CurrentMoveIndex];

                if (!string.Equals(
                        request.Uci.Trim(),
                        expectedMove,
                        StringComparison.OrdinalIgnoreCase))
                {
                    session.WrongMoves++;

                    await _context.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);

                    return ToSessionResponse(session);
                }

                // Oyuncunun hamlesini ilerlet.
                session.CurrentMoveIndex++;

                // Rakibin otomatik cevabını da ilerlet.
                if (session.CurrentMoveIndex < moves.Length)
                    session.CurrentMoveIndex++;

                if (session.CurrentMoveIndex >= moves.Length)
                {
                    session.CurrentMoveIndex = moves.Length;
                    session.IsCompleted = true;
                    RecordDailyCompletionTime(session, receivedAtUtc);
                }
                else
                {
                    session.ServerCompensationMilliseconds = AddServerCompensation(session.ServerCompensationMilliseconds, processingStarted);
                    session.CompensatedOpponentMoves++;
                    session.NextMoveAllowedAtUtc = DateTime.UtcNow.AddMilliseconds(_scoringSettings.OpponentAnimationCompensationMilliseconds);
                }

                await _context.SaveChangesAsync(cancellationToken);

                decimal? score = null;
                bool outboxCreatedForMove = false;

                if (session.IsCompleted)
                {
                    (score, outboxCreatedForMove) = await FinalizeDailySessionAsync(
                        session,
                        session.PuzzleDate,
                        cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
                if (outboxCreatedForMove)
                    _outboxSignal?.Notify();

                if (session.IsCompleted)
                {
                    await transaction.DisposeAsync();
                    await TryCreateBonusAfterDailyCommitAsync(session, cancellationToken);
                }

                return ToSessionResponse(
                    session,
                    score: score);
            }
            catch
            {
                if (_context.Database.CurrentTransaction != null)
                    await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }

        // ---------------------------------------------------------
        // GÜNLÜK BULMACA İPUCU
        // ---------------------------------------------------------

        public async Task<PuzzleSessionResponse> UseHintAsync(
            int sessionId,
            int? userId,
            string? clientId,
            CancellationToken cancellationToken = default)
        {
            clientId = NormalizeClientId(clientId);

            if (!userId.HasValue && clientId == null)
                throw new ArgumentException("CLIENT_ID_REQUIRED");

            if (sessionId <= 0)
                throw new ArgumentException("SESSION_ID_REQUIRED");

            await using var transaction =
                await _context.Database.BeginTransactionAsync(
                    cancellationToken);

            try
            {
                var session = await GetLockedDailySessionAsync(
                    sessionId,
                    userId,
                    clientId,
                    cancellationToken);

                if (session == null)
                    throw new AppException(404, "SESSION_NOT_FOUND");

                string? hintSquare = null;

                if (!session.IsCompleted)
                {
                    EnsureMoveAllowed(session.NextMoveAllowedAtUtc, DateTime.UtcNow);
                    var moves = GetMoves(session.Puzzle.Moves);

                    if (session.CurrentMoveIndex < moves.Length)
                    {
                        var expectedMove = moves[session.CurrentMoveIndex];

                        if (expectedMove.Length >= 4)
                            hintSquare = expectedMove[..2];

                        session.UsedHint = true;

                        await _context.SaveChangesAsync(cancellationToken);
                    }
                }

                await transaction.CommitAsync(cancellationToken);

                return ToSessionResponse(
                    session,
                    hintSquare);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }

        // ---------------------------------------------------------
        // BONUS BULMACA
        // ---------------------------------------------------------

        public async Task<BonusPuzzleResponse> GetBonusAsync(
            int userId,
            CancellationToken cancellationToken = default)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            var assignment = await _context.BonusPuzzleAssignments
                .Include(x => x.Puzzle)
                .Include(x => x.PuzzleAttempt)
                .FirstOrDefaultAsync(
                    x => x.UserId == userId &&
                         x.PuzzleDate == today,
                    cancellationToken);

            if (assignment == null)
            {
                return new BonusPuzzleResponse
                {
                    IsEligible = false
                };
            }

            return CreateBonusResponse(assignment);
        }

        public async Task<BonusPuzzleResponse> StartBonusAsync(
            int userId,
            CancellationToken cancellationToken = default)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            await using var transaction =
                await _context.Database.BeginTransactionAsync(
                    cancellationToken);

            try
            {
                await EnsureLockedUserAsync(userId, cancellationToken);
                await TryCreateBonusAssignmentAsync(userId, null, today, cancellationToken);

                var assignment = await GetLockedBonusAssignmentAsync(
                    userId,
                    today,
                    cancellationToken);

                if (assignment == null)
                {
                    await transaction.CommitAsync(cancellationToken);

                    return new BonusPuzzleResponse
                    {
                        IsEligible = false
                    };
                }

                if (!assignment.StartedAtUtc.HasValue &&
                    !assignment.IsCompleted)
                {
                    var user = await _context.Users
                        .AsNoTracking()
                        .Where(x => x.Id == userId)
                        .Select(x => new { x.CountryCode })
                        .FirstOrDefaultAsync(cancellationToken);

                    if (user == null)
                        throw new UnauthorizedAccessException("USER_NOT_FOUND");

                    assignment.CountryCode =
                        NormalizeCountryCode(user.CountryCode);

                    assignment.StartedAtUtc = DateTime.UtcNow;

                    await _context.SaveChangesAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);

                return CreateBonusResponse(assignment);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }

        public async Task<PuzzleSessionResponse> SubmitBonusMoveAsync(
            PuzzleMoveRequest request,
            int userId,
            CancellationToken cancellationToken = default)
        {
            var receivedAtUtc = DateTime.UtcNow;
            var today = DateOnly.FromDateTime(receivedAtUtc);
            if (string.IsNullOrWhiteSpace(request.Uci))
                throw new ArgumentException("INVALID_MOVE");

            await using var transaction =
                await _context.Database.BeginTransactionAsync(
                    cancellationToken);

            try
            {
                var assignment = await GetLockedBonusAssignmentAsync(
                    userId,
                    today,
                    cancellationToken);

                if (assignment == null)
                    throw new KeyNotFoundException("BONUS_NOT_FOUND");

                if (assignment.CompletedAtUtc.HasValue)
                    throw new AppException(409, "BONUS_ALREADY_COMPLETED");

                if (assignment.IsCompleted)
                    throw new AppException(409, "BONUS_TIME_EXPIRED");

                if (!assignment.StartedAtUtc.HasValue)
                    throw new AppException(409, "BONUS_NOT_STARTED");

                var now = receivedAtUtc;

                if (IsBonusTimeExpired(assignment, now))
                {
                    await transaction.RollbackAsync(cancellationToken);
                    await transaction.DisposeAsync();

                    _context.ChangeTracker.Clear();

                    await SubmitBonusAsync(userId, cancellationToken);

                    throw new AppException(409, "BONUS_TIME_EXPIRED");
                }

                var moves = GetMoves(assignment.Puzzle.Moves);

                if (moves.Length < 2)
                    throw new AppException(500, "PUZZLE_MOVES_INVALID");

                if (assignment.CurrentMoveIndex >= moves.Length)
                {
                    RecordBonusCompletionTime(assignment, now);

                    await _context.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);

                    return ToBonusSessionResponse(assignment);
                }

                if (request.ExpectedMoveIndex.HasValue)
                {
                    var requestedIndex = request.ExpectedMoveIndex.Value;

                    if (requestedIndex > assignment.CurrentMoveIndex)
                    {
                        throw new AppException(409, "INVALID_MOVE_INDEX");
                    }

                    if (requestedIndex < assignment.CurrentMoveIndex)
                    {
                        await transaction.CommitAsync(cancellationToken);

                        return ToBonusSessionResponse(assignment);
                    }
                }

                EnsureMoveAllowed(assignment.NextMoveAllowedAtUtc, receivedAtUtc);
                var processingStarted = Stopwatch.GetTimestamp();
                var expectedMove = moves[assignment.CurrentMoveIndex];

                if (!string.Equals(
                        request.Uci.Trim(),
                        expectedMove,
                        StringComparison.OrdinalIgnoreCase))
                {
                    assignment.WrongMoves++;
                }
                else
                {
                    assignment.CurrentMoveIndex++;

                    if (assignment.CurrentMoveIndex < moves.Length)
                        assignment.CurrentMoveIndex++;

                    if (assignment.CurrentMoveIndex >= moves.Length)
                    {
                        assignment.CurrentMoveIndex = moves.Length;
                        RecordBonusCompletionTime(assignment, now);
                    }
                    else
                    {
                        assignment.ServerCompensationMilliseconds = AddServerCompensation(assignment.ServerCompensationMilliseconds, processingStarted);
                        assignment.CompensatedOpponentMoves++;
                        assignment.NextMoveAllowedAtUtc = DateTime.UtcNow.AddMilliseconds(_scoringSettings.OpponentAnimationCompensationMilliseconds);
                    }
                }

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return ToBonusSessionResponse(assignment);
            }
            catch
            {
                if (_context.Database.CurrentTransaction != null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                }

                throw;
            }
        }

        public async Task<PuzzleSessionResponse> UseBonusHintAsync(
            int userId,
            CancellationToken cancellationToken = default)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            await using var transaction =
                await _context.Database.BeginTransactionAsync(
                    cancellationToken);

            try
            {
                var assignment = await GetLockedBonusAssignmentAsync(
                    userId,
                    today,
                    cancellationToken);

                if (assignment == null)
                    throw new KeyNotFoundException("BONUS_NOT_FOUND");

                if (assignment.IsCompleted)
                    throw new AppException(409, "BONUS_ALREADY_COMPLETED");

                if (assignment.CompletedAtUtc.HasValue)
                    throw new AppException(409, "BONUS_ALREADY_SOLVED");

                if (!assignment.StartedAtUtc.HasValue)
                    throw new AppException(409, "BONUS_NOT_STARTED");

                if (IsBonusTimeExpired(assignment))
                {
                    await transaction.RollbackAsync(cancellationToken);
                    await transaction.DisposeAsync();

                    _context.ChangeTracker.Clear();

                    await SubmitBonusAsync(userId, cancellationToken);

                    throw new AppException(409, "BONUS_TIME_EXPIRED");
                }

                EnsureMoveAllowed(assignment.NextMoveAllowedAtUtc, DateTime.UtcNow);
                var moves = GetMoves(assignment.Puzzle.Moves);

                string? hintSquare = null;

                if (assignment.CurrentMoveIndex < moves.Length)
                {
                    var expectedMove = moves[assignment.CurrentMoveIndex];

                    if (expectedMove.Length >= 4)
                        hintSquare = expectedMove[..2];

                    assignment.UsedHint = true;

                    await _context.SaveChangesAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);

                return ToBonusSessionResponse(
                    assignment,
                    hintSquare);
            }
            catch
            {
                if (_context.Database.CurrentTransaction != null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                }

                throw;
            }
        }

        // ---------------------------------------------------------
        // BONUS SONUÇLANDIRMA
        // ---------------------------------------------------------

        public async Task<BonusPuzzleSubmitResponse> SubmitBonusAsync(
            int userId,
            CancellationToken cancellationToken = default)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            await using var transaction =
                await _context.Database.BeginTransactionAsync(
                    cancellationToken);

            try
            {
                var assignment = await GetLockedBonusAssignmentAsync(
                    userId,
                    today,
                    cancellationToken);

                if (assignment == null)
                    throw new KeyNotFoundException("BONUS_NOT_FOUND");

                if (assignment.PuzzleAttemptId.HasValue &&
                    assignment.PuzzleAttempt == null)
                {
                    throw new AppException(
                        500,
                        "BONUS_ATTEMPT_NOT_FOUND");
                }

                if (assignment.PuzzleAttempt != null)
                {
                    await transaction.CommitAsync(cancellationToken);

                    return new BonusPuzzleSubmitResponse
                    {
                        Score = assignment.PuzzleAttempt.Score,
                        Seconds = assignment.PuzzleAttempt.Seconds,
                        IsCompleted = true,
                        Message = "Bonus sonucu daha önce kaydedilmiş."
                    };
                }

                if (assignment.IsCompleted)
                {
                    await transaction.CommitAsync(cancellationToken);

                    return new BonusPuzzleSubmitResponse
                    {
                        Score = 0,
                        Seconds = BonusDurationSeconds,
                        IsCompleted = true,
                        Message = "Bonus süresi doldu."
                    };
                }

                if (!assignment.StartedAtUtc.HasValue)
                    throw new AppException(409, "BONUS_NOT_STARTED");

                var now = DateTime.UtcNow;

                if (!assignment.CompletedAtUtc.HasValue)
                {
                    if (!IsBonusTimeExpired(assignment, now))
                        throw new AppException(409, "BONUS_NOT_SOLVED");

                    CalculateBonusScore(assignment, now, false);
                    assignment.IsCompleted = true;

                    await _context.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);

                    return new BonusPuzzleSubmitResponse
                    {
                        Score = 0,
                        Seconds = BonusDurationSeconds,
                        IsCompleted = true,
                        Message = "Bonus süresi doldu."
                    };
                }

                var result = CalculateBonusScore(assignment, assignment.CompletedAtUtc.Value, true);

                if (!result.IsSuccessful)
                {
                    assignment.IsCompleted = true;
                    await _context.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);

                    return new BonusPuzzleSubmitResponse
                    {
                        Score = 0m,
                        Seconds = BonusDurationSeconds,
                        IsCompleted = true,
                        Message = "Bonus süresi doldu."
                    };
                }

                var seconds = (int)Math.Min(BonusDurationSeconds,
                    Math.Ceiling(result.TotalElapsedMilliseconds / 1000m));
                decimal score = result.Score;

                var userExists = await _context.Users
                    .AnyAsync(
                        x => x.Id == userId,
                        cancellationToken);

                if (!userExists)
                    throw new UnauthorizedAccessException("USER_NOT_FOUND");

                var countryCode = assignment.CountryCode;

                var attempt = new PuzzleAttempt
                {
                    PuzzleId = assignment.PuzzleId,
                    UserId = userId,
                    ClientId = null,
                    Seconds = seconds,
                    WrongMoves = assignment.WrongMoves,
                    UsedHint = assignment.UsedHint,
                    Score = score,
                    CountryCode = countryCode,
                    PlayedOn = today,
                    SubmittedAtUtc = now
                };

                _context.PuzzleAttempts.Add(attempt);

                await _context.SaveChangesAsync(cancellationToken);

                assignment.PuzzleAttemptId = attempt.Id;
                assignment.IsCompleted = true;

                await CreateLeaderboardOutboxEntriesAsync(
                    attempt.Id,
                    userId,
                    today,
                    countryCode,
                    cancellationToken);

                await _context.SaveChangesAsync(cancellationToken);

                await transaction.CommitAsync(cancellationToken);
                _outboxSignal?.Notify();

                _logger.LogInformation(
                    "Bonus puzzle result saved. UserId: {UserId}, PuzzleId: {PuzzleId}, Score: {Score}",
                    userId,
                    assignment.PuzzleId,
                    score);

                return new BonusPuzzleSubmitResponse
                {
                    Score = score,
                    Seconds = seconds,
                    IsCompleted = true,
                    Message = "Bonus puanınız kaydedildi."
                };
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }

        // ---------------------------------------------------------
        // GÜNLÜK OTURUMU SONUÇLANDIRMA
        // ---------------------------------------------------------

        private async Task<(decimal Score, bool OutboxCreated)> FinalizeDailySessionAsync(
            PuzzleGameSession session,
            DateOnly today,
            CancellationToken cancellationToken)
        {
            if (!session.IsCompleted)
                throw new AppException(409, "SESSION_NOT_COMPLETED");

            // Her oturum kendi puanını alır.
            decimal score = CalculateDailySessionScore(session);
            await _context.SaveChangesAsync(cancellationToken);

            // Aynı bulmacayı tamamlayan oturumları sıraya al.
            // Kilit transaction tamamlanana kadar tutulur.
            var lockedPuzzle = await _context.Puzzles
                .FromSqlInterpolated(
                    $"""
                    SELECT *
                    FROM "Puzzles"
                    WHERE "Id" = {session.PuzzleId}
                    FOR UPDATE
                    """)
                .AsNoTracking()
                .FirstOrDefaultAsync(cancellationToken);

            if (lockedPuzzle == null)
                throw new KeyNotFoundException("PUZZLE_NOT_FOUND");

            var previousQuery = _context.PuzzleAttempts
                .Where(x =>
                    x.PuzzleId == session.PuzzleId &&
                    x.PlayedOn == today);

            previousQuery = session.UserId.HasValue
                ? previousQuery.Where(x =>
                    x.UserId == session.UserId.Value)
                : previousQuery.Where(x =>
                    x.UserId == null &&
                    x.ClientId == session.ClientId);

            var previousAttempt = await previousQuery
                .FirstOrDefaultAsync(cancellationToken);

            // Daha önce başarılı çözüm varsa yeni PuzzleAttempt,
            // leaderboard veya bonus kaydı oluşturma.
            if (previousAttempt != null)
                return (score, false);

            var seconds = CalculateDailySessionSeconds(session);
            var completedAt = session.CompletedAtUtc ?? DateTime.UtcNow;

            var countryCode = session.UserId.HasValue
                ? session.CountryCode
                : null;

            var attempt = new PuzzleAttempt
            {
                PuzzleId = session.PuzzleId,
                UserId = session.UserId,
                ClientId = session.UserId.HasValue
                    ? null
                    : session.ClientId,
                Seconds = seconds,
                WrongMoves = session.WrongMoves,
                UsedHint = session.UsedHint,
                Score = score,
                CountryCode = countryCode,
                PlayedOn = today,
                SubmittedAtUtc = completedAt
            };

            _context.PuzzleAttempts.Add(attempt);

            await _context.SaveChangesAsync(cancellationToken);

            // Yalnızca kayıtlı kullanıcılar leaderboard sistemine dahil edilir.
            // Misafirlerin PuzzleAttempt kayıtları tutulur ancak Outbox oluşturulmaz.
            if (session.UserId.HasValue)
            {
                await CreateLeaderboardOutboxEntriesAsync(
                    attempt.Id,
                    session.UserId.Value,
                    today,
                    countryCode,
                    cancellationToken);

                await _context.SaveChangesAsync(cancellationToken);

            }

            _logger.LogInformation(
                "Daily puzzle result saved. PuzzleId: {PuzzleId}, UserId: {UserId}, SessionId: {SessionId}, Score: {Score}",
                session.PuzzleId,
                session.UserId,
                session.Id,
                score);

            return (score, session.UserId.HasValue);
        }

        private async Task TryCreateBonusAfterDailyCommitAsync(
            PuzzleGameSession session,
            CancellationToken cancellationToken)
        {
            if (!session.UserId.HasValue || session.WrongMoves != 0 || session.UsedHint)
                return;

            try
            {
                await using var bonusTransaction = await _context.Database.BeginTransactionAsync(cancellationToken);
                await EnsureLockedUserAsync(session.UserId.Value, cancellationToken);
                await TryCreateBonusAssignmentAsync(
                    session.UserId.Value,
                    session.PuzzleId,
                    session.PuzzleDate,
                    cancellationToken);
                await bonusTransaction.CommitAsync(cancellationToken);
            }
            catch (AppException exception) when (exception.Message == "INVALID_ACCESS_TOKEN")
            {
                _context.ChangeTracker.Clear();
            }
            catch (Exception exception)
            {
                _context.ChangeTracker.Clear();
                _logger.LogError(exception,
                    "Bonus puzzle assignment failed after daily result commit. UserId: {UserId}, Date: {PuzzleDate}",
                    session.UserId,
                    session.PuzzleDate);
            }
        }

        // ---------------------------------------------------------
        // GÜNLÜK PUAN HESAPLAMA
        // ---------------------------------------------------------

        private static void EnsureMoveAllowed(DateTime? allowedAtUtc, DateTime receivedAtUtc)
        {
            if (allowedAtUtc.HasValue && receivedAtUtc < allowedAtUtc.Value)
                throw new AppException(409, "ANIMATION_IN_PROGRESS");
        }

        private long AddServerCompensation(long accumulated, long started)
        {
            var elapsed = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            return Math.Min(_scoringSettings.MaximumServerCompensationPerSessionMilliseconds,
                accumulated + Math.Min(elapsed, _scoringSettings.MaximumServerCompensationPerRequestMilliseconds));
        }

        private static void RecordBonusCompletionTime(BonusPuzzleAssignment assignment, DateTime receivedAtUtc)
        {
            var started = assignment.StartedAtUtc!.Value;
            assignment.CompletedAtUtc ??= receivedAtUtc < started ? started : receivedAtUtc;
            assignment.TotalElapsedMilliseconds ??= Math.Max(0L,
                (assignment.CompletedAtUtc.Value - started).Ticks / TimeSpan.TicksPerMillisecond);
        }

        private static void RecordDailyCompletionTime(
            PuzzleGameSession session,
            DateTime receivedAtUtc)
        {
            session.CompletedAtUtc ??= receivedAtUtc < session.StartedAtUtc
                ? session.StartedAtUtc
                : receivedAtUtc;

            session.TotalElapsedMilliseconds ??= Math.Max(
                0L,
                (session.CompletedAtUtc.Value - session.StartedAtUtc).Ticks
                    / TimeSpan.TicksPerMillisecond);
        }

        private static int CalculateDailySessionSeconds(
            PuzzleGameSession session)
        {
            long milliseconds = session.TotalElapsedMilliseconds ??
                Math.Max(0L,
                    ((session.CompletedAtUtc ?? DateTime.UtcNow) -
                     session.StartedAtUtc).Ticks / TimeSpan.TicksPerMillisecond);

            return (int)Math.Min(
                int.MaxValue,
                Math.Ceiling(milliseconds / 1000m));
        }

        private ScoringResult CalculateBonusScore(
            BonusPuzzleAssignment assignment, DateTime endAtUtc, bool solved)
        {
            int requiredPlayerMoves = GetMoves(assignment.Puzzle.Moves).Length / 2;
            if (requiredPlayerMoves <= 0)
                throw new AppException(500, "PUZZLE_MOVES_INVALID");

            long elapsed = assignment.TotalElapsedMilliseconds ?? Math.Max(0L,
                (endAtUtc - assignment.StartedAtUtc!.Value).Ticks / TimeSpan.TicksPerMillisecond);

            var result = _scoringService.Calculate(new ScoringInput
            {
                Rating = assignment.Puzzle.Rating,
                RequiredPlayerMoves = requiredPlayerMoves,
                WrongMoves = assignment.WrongMoves,
                UsedHint = assignment.UsedHint,
                TotalElapsedMilliseconds = elapsed,
                CompensatedOpponentMoves = assignment.CompensatedOpponentMoves,
                ServerCompensationMilliseconds = assignment.ServerCompensationMilliseconds,
                IsCompleted = solved,
                IsBonus = true
            });

            assignment.TotalElapsedMilliseconds = result.TotalElapsedMilliseconds;
            assignment.AnimationCompensationMilliseconds = result.AnimationCompensationMilliseconds;
            assignment.AdjustedElapsedMilliseconds = result.AdjustedElapsedMilliseconds;
            return result;
        }

        private decimal CalculateDailySessionScore(PuzzleGameSession session)
        {
            if (session.CompletedScore.HasValue) return session.CompletedScore.Value;
            var moves = GetMoves(session.Puzzle.Moves);
            int requiredPlayerMoves = moves.Length / 2;
            if (requiredPlayerMoves <= 0)
                throw new AppException(500, "PUZZLE_MOVES_INVALID");

            long elapsed = session.TotalElapsedMilliseconds ?? Math.Max(0L,
                ((session.CompletedAtUtc ?? session.StartedAtUtc) -
                 session.StartedAtUtc).Ticks / TimeSpan.TicksPerMillisecond);

            var result = _scoringService.Calculate(new ScoringInput
            {
                Rating = session.Puzzle.Rating,
                RequiredPlayerMoves = requiredPlayerMoves,
                WrongMoves = session.WrongMoves,
                UsedHint = session.UsedHint,
                TotalElapsedMilliseconds = elapsed,
                CompensatedOpponentMoves = session.CompensatedOpponentMoves,
                ServerCompensationMilliseconds = session.ServerCompensationMilliseconds,
                IsCompleted = session.IsCompleted,
                IsBonus = false
            });

            session.TotalElapsedMilliseconds ??= result.TotalElapsedMilliseconds;
            session.AnimationCompensationMilliseconds = result.AnimationCompensationMilliseconds;
            session.AdjustedElapsedMilliseconds = result.AdjustedElapsedMilliseconds;
            session.CompletedScore = result.Score;
            return result.Score;
        }

        // ---------------------------------------------------------
        // BONUS ATAMA
        // ---------------------------------------------------------

        private async Task TryCreateBonusAssignmentAsync(
            int userId,
            int? mainPuzzleId,
            DateOnly today,
            CancellationToken cancellationToken)
        {
            if (userId <= 0)
                return;

            var existing = await _context.BonusPuzzleAssignments
                .AnyAsync(
                    x => x.UserId == userId &&
                         x.PuzzleDate == today,
                    cancellationToken);

            if (existing)
                return;

            var registeredDailyPuzzleId = await _context.DailyPuzzles
                .Where(x =>
                    x.PuzzleDate == today &&
                    x.Audience == PuzzleAudience.Registered)
                .Select(x => (int?)x.PuzzleId)
                .FirstOrDefaultAsync(cancellationToken);

            if (!registeredDailyPuzzleId.HasValue)
            {
                _logger.LogWarning(
                    "Registered daily puzzle not found. UserId: {UserId}, Date: {Date}",
                    userId,
                    today);

                return;
            }

            var mainAttempt = await _context.PuzzleAttempts
                .AsNoTracking()
                .Where(x =>
                    x.UserId == userId &&
                    x.PlayedOn == today &&
                    x.PuzzleId == registeredDailyPuzzleId.Value &&
                    x.WrongMoves == 0 &&
                    !x.UsedHint)
                .OrderByDescending(x => x.SubmittedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

            if (mainAttempt == null)
                return;

            if (mainPuzzleId.HasValue &&
                mainPuzzleId.Value != registeredDailyPuzzleId.Value)
            {
                _logger.LogWarning(
                    "Submitted puzzle does not match registered daily puzzle. UserId: {UserId}",
                    userId);

                return;
            }

            var candidates = _context.Puzzles
                .AsNoTracking()
                .Where(p =>
                    p.IsActive &&
                    p.Rating > BonusMinimumRating &&
                    p.Id != registeredDailyPuzzleId.Value &&
                    !_context.PuzzleAttempts.Any(a =>
                        a.UserId == userId &&
                        a.PuzzleId == p.Id) &&
                    !_context.PuzzleGameSessions.Any(s =>
                        s.UserId == userId &&
                        s.PuzzleId == p.Id));

            var candidateCount = await candidates.CountAsync(cancellationToken);

            if (candidateCount == 0)
            {
                _logger.LogWarning(
                    "No available bonus candidates. UserId: {UserId}",
                    userId);

                return;
            }

            var selectedId = await candidates
                .OrderBy(p => p.Id)
                .Skip(Random.Shared.Next(candidateCount))
                .Select(p => p.Id)
                .FirstAsync(cancellationToken);

            var assignment = new BonusPuzzleAssignment
            {
                UserId = userId,
                PuzzleId = selectedId,
                PuzzleDate = today,
                CurrentMoveIndex = 1,
                WrongMoves = 0,
                UsedHint = false,
                IsCompleted = false,
                CreatedAtUtc = DateTime.UtcNow
            };

            _context.BonusPuzzleAssignments.Add(assignment);

            try
            {
                await _context.SaveChangesAsync(cancellationToken);

                _logger.LogInformation(
                    "Bonus puzzle assigned. UserId: {UserId}, PuzzleId: {PuzzleId}",
                    userId,
                    selectedId);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                _context.Entry(assignment).State = EntityState.Detached;

                _logger.LogInformation(
                    "Bonus assignment already created concurrently. UserId: {UserId}",
                    userId);
            }
        }

        // ---------------------------------------------------------
        // LEADERBOARD OUTBOX
        // Yalnızca kayıtlı kullanıcılar için oluşturulur.
        // ---------------------------------------------------------

        private async Task CreateLeaderboardOutboxEntriesAsync(
            int puzzleAttemptId,
            int userId,
            DateOnly today,
            string? countryCode,
            CancellationToken cancellationToken)
        {
            string leaderboardIdentity = $"user:{userId}";

            var outboxEntry = new LeaderboardCacheOutbox
            {
                PuzzleAttemptId = puzzleAttemptId,
                ClientId = leaderboardIdentity,
                UserId = userId,
                CreatedAtUtc = DateTime.UtcNow
            };

            _context.LeaderboardCacheOutboxes.Add(outboxEntry);

            var dailyPeriod = (
                Period: "daily",
                PeriodKey: today.ToString("yyyy-MM-dd"),
                StartDate: today);

            int daysSinceMonday = ((int)today.DayOfWeek + 6) % 7;
            var weeklyStartDate = today.AddDays(-daysSinceMonday);

            int isoYear = ISOWeek.GetYear(
                today.ToDateTime(TimeOnly.MinValue));

            int isoWeek = ISOWeek.GetWeekOfYear(
                today.ToDateTime(TimeOnly.MinValue));

            var weeklyPeriod = (
                Period: "weekly",
                PeriodKey: $"{isoYear}-W{isoWeek:D2}",
                StartDate: weeklyStartDate);

            var monthlyStartDate = new DateOnly(
                today.Year,
                today.Month,
                1);

            var monthlyPeriod = (
                Period: "monthly",
                PeriodKey: today.ToString("yyyy-MM"),
                StartDate: monthlyStartDate);

            var allTimePeriod = (
                Period: "alltime",
                PeriodKey: "all",
                StartDate: DateOnly.MinValue);

            var periods = new[]
            {
                dailyPeriod,
                weeklyPeriod,
                monthlyPeriod,
                allTimePeriod
            };

            foreach (var period in periods)
            {
                await CreateLeaderboardOutboxDeliveryAsync(
                    outboxEntry,
                    period.Period,
                    period.PeriodKey,
                    period.StartDate,
                    today,
                    userId,
                    null,
                    cancellationToken);

                if (countryCode != null)
                {
                    await CreateLeaderboardOutboxDeliveryAsync(
                        outboxEntry,
                        period.Period,
                        period.PeriodKey,
                        period.StartDate,
                        today,
                        userId,
                        countryCode,
                        cancellationToken);
                }
            }
        }

        private async Task CreateLeaderboardOutboxDeliveryAsync(
            LeaderboardCacheOutbox outboxEntry,
            string period,
            string periodKey,
            DateOnly startDate,
            DateOnly today,
            int userId,
            string? countryCode,
            CancellationToken cancellationToken)
        {
            // Yalnızca ilgili kayıtlı kullanıcının puanları toplanır.
            var query = _context.PuzzleAttempts
                .AsNoTracking()
                .Where(a =>
                    a.UserId == userId &&
                    a.PlayedOn >= startDate &&
                    a.PlayedOn <= today);

            if (countryCode != null)
                query = query.Where(a => a.CountryCode == countryCode);

            var aggregate = await query
                .GroupBy(a => 1)
                .Select(group => new
                {
                    TotalScore = group.Sum(a => a.Score),
                    SolvedCount = group.Count()
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (aggregate == null)
                return;

            string cacheKey =
                $"DailyChessPuzzle_leaderboard:v3:{period}:{periodKey}:{countryCode ?? "global"}";

            var delivery = new LeaderboardCacheOutboxDelivery
            {
                Outbox = outboxEntry,
                TargetClientId = $"user:{userId}",
                TargetUserId = userId,
                IsRemoval = false,
                Period = period,
                PeriodKey = periodKey,
                CountryCode = countryCode,
                CacheKey = cacheKey,
                TotalScore = aggregate.TotalScore,
                SolvedCount = aggregate.SolvedCount,
                Status = LeaderboardCacheOutboxDeliveryStatus.Pending,
                RetryCount = 0,
                CreatedAtUtc = DateTime.UtcNow
            };

            _context.LeaderboardCacheOutboxDeliveries.Add(delivery);
        }

        // ---------------------------------------------------------
        // SATIR KİLİDİ YARDIMCILARI
        // ---------------------------------------------------------

        private async Task EnsureLockedUserAsync(int? userId, CancellationToken cancellationToken)
        {
            if (!userId.HasValue)
                return;

            var user = await _context.Users
                .FromSqlInterpolated($"""
                    SELECT * FROM "Users" WHERE "Id" = {userId.Value} FOR UPDATE
                    """)
                .AsNoTracking()
                .FirstOrDefaultAsync(cancellationToken);

            if (user == null)
                throw new AppException(401, "INVALID_ACCESS_TOKEN");
        }

        private Task<PuzzleGameSession?> FindActiveDailySessionAsync(
            int? userId,
            string? clientId,
            DateOnly puzzleDate,
            CancellationToken cancellationToken)
        {
            var query = _context.PuzzleGameSessions
                .Include(x => x.Puzzle)
                .Where(x => !x.IsCompleted && x.PuzzleDate == puzzleDate);

            query = userId.HasValue
                ? query.Where(x => x.UserId == userId.Value)
                : query.Where(x => x.UserId == null && x.ClientId == clientId);

            return query.OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        private async Task<PuzzleGameSession?> GetLockedDailySessionAsync(
            int sessionId,
            int? userId,
            string? clientId,
            CancellationToken cancellationToken)
        {
            await EnsureLockedUserAsync(userId, cancellationToken);
            PuzzleGameSession? session;

            if (userId.HasValue)
            {
                session = await _context.PuzzleGameSessions
                    .FromSqlInterpolated(
                        $"""
                        SELECT *
                        FROM "PuzzleGameSessions"
                        WHERE "Id" = {sessionId}
                          AND "UserId" = {userId.Value}
                        FOR UPDATE
                        """)
                    .FirstOrDefaultAsync(cancellationToken);
            }
            else
            {
                session = await _context.PuzzleGameSessions
                    .FromSqlInterpolated(
                        $"""
                        SELECT *
                        FROM "PuzzleGameSessions"
                        WHERE "Id" = {sessionId}
                          AND "UserId" IS NULL
                          AND "ClientId" = {clientId}
                        FOR UPDATE
                        """)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            if (session == null)
                return null;

            await _context.Entry(session)
                .Reference(x => x.Puzzle)
                .LoadAsync(cancellationToken);

            return session;
        }

        private async Task<BonusPuzzleAssignment?> GetLockedBonusAssignmentAsync(
            int userId,
            DateOnly today,
            CancellationToken cancellationToken)
        {
            await EnsureLockedUserAsync(userId, cancellationToken);
            var assignment = await _context.BonusPuzzleAssignments
                .FromSqlInterpolated(
                    $"""
                    SELECT *
                    FROM "BonusPuzzleAssignments"
                    WHERE "UserId" = {userId}
                      AND "PuzzleDate" = {today}
                    FOR UPDATE
                    """)
                .FirstOrDefaultAsync(cancellationToken);

            if (assignment != null)
            {
                await _context.Entry(assignment)
                    .Reference(x => x.Puzzle)
                    .LoadAsync(cancellationToken);

                await _context.Entry(assignment)
                    .Reference(x => x.PuzzleAttempt)
                    .LoadAsync(cancellationToken);
            }

            return assignment;
        }

        // ---------------------------------------------------------
        // YARDIMCI METOTLAR
        // ---------------------------------------------------------

        private async Task<Puzzle?> GetDailyPuzzleAsync(
            int? userId,
            DateOnly today,
            CancellationToken cancellationToken)
        {
            var audience = userId.HasValue
                ? PuzzleAudience.Registered
                : PuzzleAudience.Guest;

            return await _context.DailyPuzzles
                .Where(x =>
                    x.PuzzleDate == today &&
                    x.Audience == audience &&
                    x.Puzzle.IsActive)
                .Select(x => x.Puzzle)
                .FirstOrDefaultAsync(cancellationToken);
        }

        private static string? NormalizeClientId(string? clientId)
        {
            return string.IsNullOrWhiteSpace(clientId)
                ? null
                : clientId.Trim();
        }

        private static string? NormalizeCountryCode(string? countryCode)
        {
            return string.IsNullOrWhiteSpace(countryCode)
                ? null
                : countryCode.Trim().ToUpperInvariant();
        }

        private static string[] GetMoves(string moves)
        {
            return moves.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);
        }

        private static bool IsUniqueViolation(DbUpdateException exception)
        {
            return exception.InnerException is PostgresException postgres &&
                   postgres.SqlState == PostgresErrorCodes.UniqueViolation;
        }

        private PuzzleSessionResponse ToSessionResponse(
            PuzzleGameSession session,
            string? hintSquare = null,
            decimal? score = null)
        {
            var moves = GetMoves(session.Puzzle.Moves);

            var playedMoveCount = Math.Clamp(
                session.CurrentMoveIndex,
                0,
                moves.Length);

            return new PuzzleSessionResponse
            {
                SessionId = session.Id,
                PuzzleId = session.PuzzleId,
                Fen = session.Puzzle.Fen,
                PlayedMoves = moves.Take(playedMoveCount).ToList(),
                CurrentMoveIndex = session.CurrentMoveIndex,
                WrongMoves = session.WrongMoves,
                UsedHint = session.UsedHint,
                IsSolved = session.IsCompleted,
                IsCompleted = session.IsCompleted,
                StartedAtUtc = session.StartedAtUtc,
                HintSquare = hintSquare,
                Score = score
            };
        }

        private PuzzleSessionResponse ToBonusSessionResponse(
            BonusPuzzleAssignment assignment,
            string? hintSquare = null)
        {
            var moves = GetMoves(assignment.Puzzle.Moves);

            var playedMoveCount = Math.Clamp(
                assignment.CurrentMoveIndex,
                0,
                moves.Length);

            return new PuzzleSessionResponse
            {
                PuzzleId = assignment.PuzzleId,
                PlayedMoves = moves.Take(playedMoveCount).ToList(),
                CurrentMoveIndex = assignment.CurrentMoveIndex,
                WrongMoves = assignment.WrongMoves,
                UsedHint = assignment.UsedHint,
                IsSolved = assignment.CompletedAtUtc.HasValue,
                IsCompleted = assignment.IsCompleted,
                StartedAtUtc = assignment.StartedAtUtc ?? DateTime.UtcNow,
                HintSquare = hintSquare,
                RemainingSeconds = GetBonusRemainingSeconds(assignment),
                Score = assignment.PuzzleAttempt?.Score
            };
        }

        private bool IsBonusTimeExpired(
            BonusPuzzleAssignment assignment,
            DateTime? currentTimeUtc = null)
        {
            if (!assignment.StartedAtUtc.HasValue)
                return false;

            if (assignment.CompletedAtUtc.HasValue)
            {
                return (
                    assignment.CompletedAtUtc.Value -
                    assignment.StartedAtUtc.Value
                ).TotalSeconds >= BonusDurationSeconds;
            }

            var now = currentTimeUtc ?? DateTime.UtcNow;

            return (
                now - assignment.StartedAtUtc.Value
            ).TotalSeconds >= BonusDurationSeconds;
        }

        private int GetBonusRemainingSeconds(
            BonusPuzzleAssignment assignment)
        {
            if (!assignment.StartedAtUtc.HasValue)
                return BonusDurationSeconds;

            if (assignment.IsCompleted ||
                assignment.CompletedAtUtc.HasValue)
                return 0;

            var elapsed = (
                DateTime.UtcNow - assignment.StartedAtUtc.Value
            ).TotalSeconds;

            return Math.Clamp(
                (int)Math.Ceiling(BonusDurationSeconds - elapsed),
                0,
                BonusDurationSeconds);
        }

        private BonusPuzzleResponse CreateBonusResponse(
            BonusPuzzleAssignment assignment)
        {
            var moves = GetMoves(assignment.Puzzle.Moves);

            var playedMoveCount = Math.Clamp(
                assignment.CurrentMoveIndex,
                0,
                moves.Length);

            var isExpired = assignment.StartedAtUtc.HasValue &&
                            assignment.PuzzleAttempt == null &&
                            IsBonusTimeExpired(assignment);

            return new BonusPuzzleResponse
            {
                IsEligible = true,
                IsCompleted = assignment.IsCompleted,
                IsExpired = isExpired,
                PuzzleId = assignment.PuzzleId,
                Fen = assignment.StartedAtUtc.HasValue
                    ? assignment.Puzzle.Fen
                    : null,
                PlayedMoves = moves.Take(playedMoveCount).ToList(),
                CurrentMoveIndex = assignment.CurrentMoveIndex,
                WrongMoves = assignment.WrongMoves,
                UsedHint = assignment.UsedHint,
                StartedAtUtc = assignment.StartedAtUtc,
                CompletedAtUtc = assignment.CompletedAtUtc,
                RemainingSeconds = GetBonusRemainingSeconds(assignment),
                Score = assignment.PuzzleAttempt?.Score
            };
        }

        private static string GetDifficulty(int rating)
        {
            if (rating <= 1000)
                return "Kolay";

            if (rating <= 2000)
                return "Orta";

            return "Zor";
        }
    }
}
