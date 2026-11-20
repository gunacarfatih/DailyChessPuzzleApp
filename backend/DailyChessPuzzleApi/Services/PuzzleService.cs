
using System.Globalization;
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
        private const int BonusDurationSeconds = 300;
        private const int BonusMinimumRating = 2500;

        private readonly ApplicationDbContext _context;
        private readonly ILogger<PuzzleService> _logger;
        private readonly IDailyPuzzleService _dailyPuzzleService;

        public PuzzleService(
            ApplicationDbContext context,
            ILogger<PuzzleService> logger,
            IDailyPuzzleService dailyPuzzleService)
        {
            _context = context;
            _logger = logger;
            _dailyPuzzleService = dailyPuzzleService;
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
                Fen = puzzle.Fen,
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

            var existingSession = await FindActiveDailySessionAsync(
                userId,
                clientId,
                today,
                cancellationToken);
            if (existingSession != null)
                return ToSessionResponse(existingSession);

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

            if (moves.Length == 0)
                throw new AppException(500, "PUZZLE_MOVES_INVALID");

            var now = DateTime.UtcNow;

            var session = new PuzzleGameSession
            {
                PuzzleId = puzzle.Id,
                Puzzle = puzzle,
                UserId = userId,
                ClientId = userId.HasValue ? null : clientId,
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
                    return ToSessionResponse(concurrentSession);
                throw;
            }

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
                    await TryCreateBonusAfterDailyCommitAsync(session, cancellationToken);

                    return ToSessionResponse(
                        session,
                        score: completedScore);
                }

                var moves = GetMoves(session.Puzzle.Moves);

                if (moves.Length == 0)
                    throw new AppException(500, "PUZZLE_MOVES_INVALID");

                if (session.CurrentMoveIndex == 0)
                    session.CurrentMoveIndex = 1;

                if (session.CurrentMoveIndex >= moves.Length)
                {
                    session.IsCompleted = true;
                    session.CompletedAtUtc = DateTime.UtcNow;

                    await _context.SaveChangesAsync(cancellationToken);

                    var completedScore = await FinalizeDailySessionAsync(
                        session,
                        session.PuzzleDate,
                        cancellationToken);

                    await transaction.CommitAsync(cancellationToken);
                    await TryCreateBonusAfterDailyCommitAsync(session, cancellationToken);

                    return ToSessionResponse(
                        session,
                        score: completedScore);
                }

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
                    session.CompletedAtUtc = DateTime.UtcNow;
                }

                await _context.SaveChangesAsync(cancellationToken);

                int? score = null;

                if (session.IsCompleted)
                {
                    score = await FinalizeDailySessionAsync(
                        session,
                        session.PuzzleDate,
                        cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);

                if (session.IsCompleted)
                    await TryCreateBonusAfterDailyCommitAsync(session, cancellationToken);

                return ToSessionResponse(
                    session,
                    score: score);
            }
            catch
            {
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

            await TryCreateBonusAssignmentAsync(
                userId,
                null,
                today,
                cancellationToken);

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

                if (assignment.CompletedAtUtc.HasValue)
                    throw new AppException(409, "BONUS_ALREADY_COMPLETED");

                if (assignment.IsCompleted)
                    throw new AppException(409, "BONUS_TIME_EXPIRED");

                if (!assignment.StartedAtUtc.HasValue)
                    throw new AppException(409, "BONUS_NOT_STARTED");

                var now = DateTime.UtcNow;

                if (IsBonusTimeExpired(assignment, now))
                {
                    await transaction.RollbackAsync(cancellationToken);

                    _context.ChangeTracker.Clear();

                    await SubmitBonusAsync(userId, cancellationToken);

                    throw new AppException(409, "BONUS_TIME_EXPIRED");
                }

                var moves = GetMoves(assignment.Puzzle.Moves);

                if (moves.Length == 0)
                    throw new AppException(500, "PUZZLE_MOVES_INVALID");

                if (assignment.CurrentMoveIndex >= moves.Length)
                {
                    assignment.CompletedAtUtc = now;

                    await _context.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);

                    return ToBonusSessionResponse(assignment);
                }

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
                        assignment.CompletedAtUtc = now;
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

                    _context.ChangeTracker.Clear();

                    await SubmitBonusAsync(userId, cancellationToken);

                    throw new AppException(409, "BONUS_TIME_EXPIRED");
                }

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
                var assignment = await _context.BonusPuzzleAssignments
                    .FromSqlInterpolated(
                        $"""
                        SELECT *
                        FROM "BonusPuzzleAssignments"
                        WHERE "UserId" = {userId}
                          AND "PuzzleDate" = {today}
                        FOR UPDATE
                        """)
                    .Include(x => x.Puzzle)
                    .Include(x => x.PuzzleAttempt)
                    .FirstOrDefaultAsync(cancellationToken);

                if (assignment == null)
                    throw new KeyNotFoundException("BONUS_NOT_FOUND");

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

                var elapsed = (
                    assignment.CompletedAtUtc.Value -
                    assignment.StartedAtUtc.Value
                ).TotalSeconds;

                if (elapsed >= BonusDurationSeconds)
                {
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

                var seconds = Math.Clamp(
                    (int)Math.Ceiling(elapsed),
                    0,
                    BonusDurationSeconds);

                long timePenalty = (long)seconds * 2;
                long wrongMovePenalty = (long)assignment.WrongMoves * 150;
                long hintPenalty = assignment.UsedHint ? 250 : 0;

                int score = (int)Math.Max(
                    0L,
                    1000L -
                    timePenalty -
                    wrongMovePenalty -
                    hintPenalty);

                var user = await _context.Users
                    .Where(x => x.Id == userId)
                    .Select(x => new { x.CountryCode })
                    .FirstOrDefaultAsync(cancellationToken);

                if (user == null)
                    throw new UnauthorizedAccessException("USER_NOT_FOUND");

                var countryCode = NormalizeCountryCode(user.CountryCode);

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

        private async Task<int> FinalizeDailySessionAsync(
            PuzzleGameSession session,
            DateOnly today,
            CancellationToken cancellationToken)
        {
            if (!session.IsCompleted)
                throw new AppException(409, "SESSION_NOT_COMPLETED");

            // Her oturum kendi puanını alır.
            int score = CalculateDailySessionScore(session);

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
                return score;

            var seconds = CalculateDailySessionSeconds(session);
            var completedAt = session.CompletedAtUtc ?? DateTime.UtcNow;

            string? countryCode = null;

            if (session.UserId.HasValue)
            {
                countryCode = await _context.Users
                    .Where(x => x.Id == session.UserId.Value)
                    .Select(x => x.CountryCode)
                    .FirstOrDefaultAsync(cancellationToken);

                countryCode = NormalizeCountryCode(countryCode);
            }

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

            return score;
        }

        private async Task TryCreateBonusAfterDailyCommitAsync(
            PuzzleGameSession session,
            CancellationToken cancellationToken)
        {
            if (!session.UserId.HasValue || session.WrongMoves != 0 || session.UsedHint)
                return;

            try
            {
                await TryCreateBonusAssignmentAsync(
                    session.UserId.Value,
                    session.PuzzleId,
                    session.PuzzleDate,
                    cancellationToken);
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

        private static int CalculateDailySessionSeconds(
            PuzzleGameSession session)
        {
            var completedAt =
                session.CompletedAtUtc ?? DateTime.UtcNow;

            var elapsed = (
                completedAt - session.StartedAtUtc
            ).TotalSeconds;

            return Math.Max(
                0,
                (int)Math.Min(
                    int.MaxValue,
                    Math.Ceiling(elapsed)));
        }

        private static int CalculateDailySessionScore(
            PuzzleGameSession session)
        {
            var seconds = CalculateDailySessionSeconds(session);

            long timePenalty = (long)seconds * 2;
            long wrongMovePenalty = (long)session.WrongMoves * 150;
            long hintPenalty = session.UsedHint ? 250 : 0;

            return (int)Math.Max(
                0L,
                1000L -
                timePenalty -
                wrongMovePenalty -
                hintPenalty);
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

            var candidateIds = await _context.Puzzles
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
                        s.PuzzleId == p.Id))
                .Select(p => p.Id)
                .ToListAsync(cancellationToken);

            if (candidateIds.Count == 0)
            {
                _logger.LogWarning(
                    "No available bonus candidates. UserId: {UserId}",
                    userId);

                return;
            }

            var selectedId = candidateIds[
                Random.Shared.Next(candidateIds.Count)];

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
                $"DailyChessPuzzle_leaderboard:v2:{period}:{periodKey}:{countryCode ?? "global"}";

            var delivery = new LeaderboardCacheOutboxDelivery
            {
                Outbox = outboxEntry,
                TargetClientId = $"user:{userId}",
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
            int? score = null)
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

        private static PuzzleSessionResponse ToBonusSessionResponse(
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

        private static bool IsBonusTimeExpired(
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

        private static int GetBonusRemainingSeconds(
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

        private static BonusPuzzleResponse CreateBonusResponse(
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
                Fen = assignment.Puzzle.Fen,
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
