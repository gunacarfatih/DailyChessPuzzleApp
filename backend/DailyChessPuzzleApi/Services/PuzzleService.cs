
using System.Globalization;
using DailyChessPuzzleApi.Data;
using DailyChessPuzzleApi.DTOs;
using DailyChessPuzzleApi.Models;
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

        public PuzzleService(
            ApplicationDbContext context,
            ILogger<PuzzleService> logger)
        {
            _context = context;
            _logger = logger;
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
                _logger.LogWarning(
                    "No active puzzle found. Date: {Date}, Audience: {Audience}",
                    today,
                    audience);

                return null;
            }

            var attemptsQuery = _context.PuzzleAttempts
                .AsNoTracking()
                .Where(a => a.PuzzleId == puzzle.Id);

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
                Moves = puzzle.Moves,
                Difficulty = GetDifficulty(puzzle.Rating),
                IsSolved = previousAttempt != null,
                PreviousScore = previousAttempt?.Score
            };
        }

        // ---------------------------------------------------------
        // ANA BULMACA OTURUMU
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
                throw new KeyNotFoundException("Today's puzzle was not found.");

            var query = _context.PuzzleGameSessions
                .Include(x => x.Puzzle)
                .Where(x =>
                    x.PuzzleDate == today &&
                    x.PuzzleId == puzzle.Id);

            query = userId.HasValue
                ? query.Where(x => x.UserId == userId.Value)
                : query.Where(x => x.UserId == null &&
                                   x.ClientId == clientId);

            var session = await query.FirstOrDefaultAsync(cancellationToken);

            if (session == null)
            {
                session = new PuzzleGameSession
                {
                    PuzzleId = puzzle.Id,
                    UserId = userId,
                    ClientId = userId.HasValue ? null : clientId,
                    PuzzleDate = today,
                    CurrentMoveIndex = 0,
                    WrongMoves = 0,
                    UsedHint = false,
                    StartedAtUtc = DateTime.UtcNow,
                    CreatedAtUtc = DateTime.UtcNow
                };

                _context.PuzzleGameSessions.Add(session);

                try
                {
                    await _context.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException ex) when (IsUniqueViolation(ex))
                {
                    _context.Entry(session).State = EntityState.Detached;

                    var existingQuery = _context.PuzzleGameSessions
                        .Where(x =>
                            x.PuzzleDate == today &&
                            x.PuzzleId == puzzle.Id);

                    existingQuery = userId.HasValue
                        ? existingQuery.Where(x => x.UserId == userId.Value)
                        : existingQuery.Where(x =>
                            x.UserId == null && x.ClientId == clientId);

                    session = await existingQuery
                        .FirstAsync(cancellationToken);
                }
            }

            return ToSessionResponse(session);
        }


        public async Task<PuzzleSessionResponse> SubmitMoveAsync(
            PuzzleMoveRequest request,
            int? userId,
            CancellationToken cancellationToken = default)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var clientId = NormalizeClientId(request.ClientId);

            if (!userId.HasValue && clientId == null)
                throw new ArgumentException("CLIENT_ID_REQUIRED");

            var puzzle = await GetDailyPuzzleAsync(
                userId,
                today,
                cancellationToken);

            if (puzzle == null)
                throw new KeyNotFoundException("Today's puzzle was not found.");

            var query = _context.PuzzleGameSessions
                .Include(x => x.Puzzle)
                .Where(x =>
                    x.PuzzleDate == today &&
                    x.PuzzleId == puzzle.Id);

            query = userId.HasValue
                ? query.Where(x => x.UserId == userId.Value)
                : query.Where(x => x.UserId == null &&
                                   x.ClientId == clientId);

            var session = await query.FirstOrDefaultAsync(cancellationToken);

            if (session == null)
                throw new InvalidOperationException("SESSION_NOT_STARTED");

            if (session.IsCompleted)
                return ToSessionResponse(session);

            var moves = GetMoves(session.Puzzle.Moves);

            if (session.CurrentMoveIndex >= moves.Length)
            {
                session.IsCompleted = true;
                session.CompletedAtUtc = DateTime.UtcNow;

                await _context.SaveChangesAsync(cancellationToken);

                if (session.UserId.HasValue &&
                    session.WrongMoves == 0 &&
                    !session.UsedHint)
                {
                    await TryCreateBonusAssignmentAsync(
                        session.UserId.Value,
                        session.PuzzleId,
                        today,
                        cancellationToken);
                }

                return ToSessionResponse(session);
            }

            var expectedMove = moves[session.CurrentMoveIndex];

            if (!string.Equals(
                    request.Uci.Trim(),
                    expectedMove,
                    StringComparison.OrdinalIgnoreCase))
            {
                session.WrongMoves++;

                await _context.SaveChangesAsync(cancellationToken);

                return ToSessionResponse(session);
            }

            // Kullanıcının doğru hamlesi ve rakibin otomatik cevabı
            // birlikte iki indeks ilerletir.
            session.CurrentMoveIndex += 2;

            if (session.CurrentMoveIndex >= moves.Length)
            {
                session.IsCompleted = true;
                session.CompletedAtUtc = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync(cancellationToken);

            if (session.IsCompleted &&
                session.UserId.HasValue &&
                session.WrongMoves == 0 &&
                !session.UsedHint)
            {
                await TryCreateBonusAssignmentAsync(
                    session.UserId.Value,
                    session.PuzzleId,
                    today,
                    cancellationToken);
            }

            return ToSessionResponse(session);
        }


        public async Task<PuzzleSessionResponse> UseHintAsync(
            int? userId,
            string? clientId,
            CancellationToken cancellationToken = default)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            clientId = NormalizeClientId(clientId);

            if (!userId.HasValue && clientId == null)
                throw new ArgumentException("CLIENT_ID_REQUIRED");

            var query = _context.PuzzleGameSessions
                .Where(x => x.PuzzleDate == today);

            query = userId.HasValue
                ? query.Where(x => x.UserId == userId.Value)
                : query.Where(x => x.UserId == null &&
                                   x.ClientId == clientId);

            var session = await query.FirstOrDefaultAsync(cancellationToken);

            if (session == null)
                throw new InvalidOperationException("SESSION_NOT_STARTED");

            if (!session.IsCompleted)
            {
                session.UsedHint = true;
                await _context.SaveChangesAsync(cancellationToken);
            }

            return ToSessionResponse(session);
        }

        // ---------------------------------------------------------
        // BONUS BULMACA
        // ---------------------------------------------------------

        public async Task<BonusPuzzleResponse> GetBonusAsync(
            int userId,
            CancellationToken cancellationToken = default)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            await TryCreateBonusAssignmentAsync(
                userId,
                null,
                today,
                cancellationToken);

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

            // Çözülmemiş ve süresi dolmuş bonusu sonuçlandır.
            // Tamamlanmış bir bulmacada ise son hamlenin zamanı esas alınır.
            if (!assignment.IsCompleted &&
                !assignment.CompletedAtUtc.HasValue &&
                assignment.StartedAtUtc.HasValue &&
                IsBonusTimeExpired(assignment))
            {
                await SubmitBonusAsync(userId, cancellationToken);

                assignment = await _context.BonusPuzzleAssignments
                    .Include(x => x.Puzzle)
                    .Include(x => x.PuzzleAttempt)
                    .FirstAsync(
                        x => x.UserId == userId &&
                             x.PuzzleDate == today,
                        cancellationToken);
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

            // Süre yalnızca ilk başlatmada başlar.
            // Tekrar çağrılması süreyi sıfırlamaz.
            if (!assignment.StartedAtUtc.HasValue &&
                !assignment.IsCompleted)
            {
                assignment.StartedAtUtc = DateTime.UtcNow;

                await _context.SaveChangesAsync(cancellationToken);
            }

            if (!assignment.IsCompleted &&
                !assignment.CompletedAtUtc.HasValue &&
                IsBonusTimeExpired(assignment))
            {
                await SubmitBonusAsync(userId, cancellationToken);

                assignment = await _context.BonusPuzzleAssignments
                    .Include(x => x.Puzzle)
                    .Include(x => x.PuzzleAttempt)
                    .FirstAsync(
                        x => x.UserId == userId &&
                             x.PuzzleDate == today,
                        cancellationToken);
            }

            return CreateBonusResponse(assignment);
        }

        
public async Task<PuzzleSessionResponse> SubmitBonusMoveAsync(
    PuzzleMoveRequest request,
    int userId,
    CancellationToken cancellationToken = default)
{
    var today = DateOnly.FromDateTime(DateTime.UtcNow);

    var assignment = await _context.BonusPuzzleAssignments
        .Include(x => x.Puzzle)
        .FirstOrDefaultAsync(
            x => x.UserId == userId &&
                 x.PuzzleDate == today,
            cancellationToken);

    if (assignment == null)
        throw new KeyNotFoundException("BONUS_NOT_FOUND");

    // Bonus başarıyla çözülmüşse tekrar hamle kabul etme.
    if (assignment.CompletedAtUtc.HasValue)
        throw new InvalidOperationException("BONUS_ALREADY_COMPLETED");

    // Bonus süresi dolduğu için kapatılmışsa farklı hata döndür.
    if (assignment.IsCompleted)
        throw new InvalidOperationException("BONUS_TIME_EXPIRED");

    if (!assignment.StartedAtUtc.HasValue)
        throw new InvalidOperationException("BONUS_NOT_STARTED");

    var now = DateTime.UtcNow;

    if (IsBonusTimeExpired(assignment, now))
    {
        await SubmitBonusAsync(userId, cancellationToken);

        throw new InvalidOperationException("BONUS_TIME_EXPIRED");
    }

    var moves = GetMoves(assignment.Puzzle.Moves);

    if (assignment.CurrentMoveIndex >= moves.Length)
    {
        assignment.CompletedAtUtc = now;

        await _context.SaveChangesAsync(cancellationToken);

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
        assignment.CurrentMoveIndex += 2;

        if (assignment.CurrentMoveIndex >= moves.Length)
        {
            // Son hamlenin sunucuya ulaştığı an.
            assignment.CompletedAtUtc = now;
        }
    }

    await _context.SaveChangesAsync(cancellationToken);

    return ToBonusSessionResponse(assignment);
}

        public async Task<PuzzleSessionResponse> UseBonusHintAsync(
            int userId,
            CancellationToken cancellationToken = default)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            var assignment = await _context.BonusPuzzleAssignments
                .FirstOrDefaultAsync(
                    x => x.UserId == userId &&
                         x.PuzzleDate == today,
                    cancellationToken);

            if (assignment == null)
                throw new KeyNotFoundException("BONUS_NOT_FOUND");

            if (assignment.IsCompleted)
                throw new InvalidOperationException("BONUS_ALREADY_COMPLETED");

            if (assignment.CompletedAtUtc.HasValue)
                throw new InvalidOperationException("BONUS_ALREADY_SOLVED");

            if (!assignment.StartedAtUtc.HasValue)
                throw new InvalidOperationException("BONUS_NOT_STARTED");

            if (IsBonusTimeExpired(assignment))
            {
                await SubmitBonusAsync(userId, cancellationToken);

                throw new InvalidOperationException("BONUS_TIME_EXPIRED");
            }

            assignment.UsedHint = true;

            await _context.SaveChangesAsync(cancellationToken);

            return ToBonusSessionResponse(assignment);
        }

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
                // Aynı bonus için eşzamanlı sonuç isteklerini sıraya al.
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

                // Daha önce kaydedilmiş sonuç varsa aynı sonucu döndür.
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
                    throw new InvalidOperationException("BONUS_NOT_STARTED");

                var now = DateTime.UtcNow;

                // Bulmaca henüz çözülmediyse, süre dolmadan
                // sonuçlandırılmasına izin verme.
                if (!assignment.CompletedAtUtc.HasValue)
                {
                    if (!IsBonusTimeExpired(assignment, now))
                        throw new InvalidOperationException("BONUS_NOT_SOLVED");

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

                // Süre hesabında submit isteğinin zamanı değil,
                // son hamlenin tamamlanma zamanı kullanılır.
                var elapsed =
                    (assignment.CompletedAtUtc.Value -
                     assignment.StartedAtUtc.Value).TotalSeconds;

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

                // Mevcut puanlama formülü korunuyor.
                long timePenalty = (long)seconds * 2;
                long wrongMovePenalty = (long)assignment.WrongMoves * 150;
                long hintPenalty = assignment.UsedHint ? 250 : 0;

                int score = (int)Math.Max(
                    0L,
                    1000L - timePenalty - wrongMovePenalty - hintPenalty);

                var user = await _context.Users
                    .Where(x => x.Id == userId)
                    .Select(x => new { x.CountryCode })
                    .FirstOrDefaultAsync(cancellationToken);

                if (user == null)
                    throw new UnauthorizedAccessException("USER_NOT_FOUND");

                var countryCode = string.IsNullOrWhiteSpace(user.CountryCode)
                    ? null
                    : user.CountryCode.Trim().ToUpperInvariant();

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

                // Bonus puanı da mevcut leaderboard sistemine eklenir.
                await CreateLeaderboardOutboxEntriesAsync(
                    attempt.Id,
                    userId,
                    null,
                    $"user:{userId}",
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
        // BONUS ATAMA
        // ---------------------------------------------------------



        private async Task TryCreateBonusAssignmentAsync(
            int userId,
            int? mainPuzzleId,
            DateOnly today,
            CancellationToken cancellationToken)
        {
            // Kullanıcının bugün için bonus ataması zaten var mı?
            var existing = await _context.BonusPuzzleAssignments
                .AnyAsync(
                    x => x.UserId == userId &&
                         x.PuzzleDate == today,
                    cancellationToken);

            if (existing)
            {
                _logger.LogInformation(
                    "BONUS CHECK: Assignment already exists. UserId: {UserId}",
                    userId);

                return;
            }

            // Önce kayıtlı kullanıcıların günlük bulmacasını bul.
            var registeredDailyPuzzleId = await _context.DailyPuzzles
                .Where(x =>
                    x.PuzzleDate == today &&
                    x.Audience == PuzzleAudience.Registered)
                .Select(x => (int?)x.PuzzleId)
                .FirstOrDefaultAsync(cancellationToken);

            if (!registeredDailyPuzzleId.HasValue)
            {
                _logger.LogWarning(
                    "BONUS CHECK FAILED: Registered daily puzzle not found. " +
                    "UserId: {UserId}, Date: {Date}",
                    userId,
                    today);

                return;
            }

            // Günlük bulmacanın kusursuz çözüm kaydını PuzzleAttempts
            // tablosundan kontrol et.
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
            {
                _logger.LogWarning(
                    "BONUS CHECK FAILED: No flawless completed attempt. " +
                    "UserId: {UserId}, Date: {Date}, DailyPuzzleId: {DailyPuzzleId}",
                    userId,
                    today,
                    registeredDailyPuzzleId.Value);

                return;
            }

            _logger.LogInformation(
                "BONUS CHECK: Flawless attempt found. " +
                "UserId: {UserId}, PuzzleId: {PuzzleId}, " +
                "WrongMoves: {WrongMoves}, UsedHint: {UsedHint}, Score: {Score}",
                userId,
                mainAttempt.PuzzleId,
                mainAttempt.WrongMoves,
                mainAttempt.UsedHint,
                mainAttempt.Score);

            // Submit çağrısından gelen bulmaca ID'si de doğrulansın.
            if (mainPuzzleId.HasValue &&
                mainPuzzleId.Value != registeredDailyPuzzleId.Value)
            {
                _logger.LogWarning(
                    "BONUS CHECK FAILED: Submitted puzzle does not match daily puzzle. " +
                    "SubmittedPuzzleId: {SubmittedPuzzleId}, DailyPuzzleId: {DailyPuzzleId}",
                    mainPuzzleId.Value,
                    registeredDailyPuzzleId.Value);

                return;
            }

            // Bonus için günlük bulmaca dışında uygun bir puzzle seç.
            // Kullanıcının daha önce denediği bulmacaları tekrar verme.
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
                    "BONUS CHECK FAILED: No available bonus candidates. " +
                    "UserId: {UserId}, MinimumRating: {MinimumRating}",
                    userId,
                    BonusMinimumRating);

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
                    "BONUS ASSIGNED SUCCESSFULLY. " +
                    "UserId: {UserId}, PuzzleId: {PuzzleId}",
                    userId,
                    selectedId);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                _context.Entry(assignment).State = EntityState.Detached;

                _logger.LogInformation(
                    "BONUS CHECK: Assignment already created concurrently. " +
                    "UserId: {UserId}, Date: {Date}",
                    userId,
                    today);
            }
        }



        // ---------------------------------------------------------
        // ANA BULMACA SUBMIT
        // ---------------------------------------------------------

        public async Task<PuzzleSubmitResponse> SubmitAsync(
            PuzzleSubmitRequest request,
            int? userId,
            CancellationToken cancellationToken = default)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            string? clientId = NormalizeClientId(request.ClientId);

            if (!userId.HasValue && clientId == null)
                throw new ArgumentException("CLIENT_ID_REQUIRED");

            string? leaderboardIdentity = userId.HasValue
                ? $"user:{userId.Value}"
                : null;

            string? countryCode;

            if (userId.HasValue)
            {
                var user = await _context.Users
                    .Where(x => x.Id == userId.Value)
                    .Select(x => new { x.CountryCode })
                    .FirstOrDefaultAsync(cancellationToken);

                if (user == null)
                    throw new UnauthorizedAccessException("USER_NOT_FOUND");

                countryCode = string.IsNullOrWhiteSpace(user.CountryCode)
                    ? request.CountryCode
                    : user.CountryCode;
            }
            else
            {
                countryCode = request.CountryCode;
            }

            countryCode = string.IsNullOrWhiteSpace(countryCode)
                ? null
                : countryCode.Trim().ToUpperInvariant();

            var audience = userId.HasValue
                ? PuzzleAudience.Registered
                : PuzzleAudience.Guest;

            var puzzle = await _context.DailyPuzzles
                .Where(dp =>
                    dp.PuzzleId == request.PuzzleId &&
                    dp.PuzzleDate == today &&
                    dp.Audience == audience &&
                    dp.Puzzle.IsActive)
                .Select(dp => dp.Puzzle)
                .FirstOrDefaultAsync(cancellationToken);

            if (puzzle == null)
                throw new KeyNotFoundException("Today's puzzle was not found.");

            var previousQuery = _context.PuzzleAttempts
                .AsNoTracking()
                .Where(a => a.PuzzleId == request.PuzzleId);

            previousQuery = userId.HasValue
                ? previousQuery.Where(a => a.UserId == userId.Value)
                : previousQuery.Where(a =>
                    a.UserId == null && a.ClientId == clientId);

            var previousAttempt = await previousQuery
                .FirstOrDefaultAsync(cancellationToken);

            if (previousAttempt != null)
            {
                return CreateSubmitResponse(
                    previousAttempt,
                    "Bu bulmacayı daha önce çözdün. Önceki puanın gösteriliyor.");
            }

            long timePenalty = (long)request.Seconds * 2;
            long wrongMovePenalty = (long)request.WrongMoves * 150;
            long hintPenalty = request.UsedHint ? 250 : 0;

            int score = (int)Math.Max(
                0L,
                1000L - timePenalty - wrongMovePenalty - hintPenalty);

            var attempt = new PuzzleAttempt
            {
                PuzzleId = puzzle.Id,
                ClientId = clientId,
                UserId = userId,
                Seconds = request.Seconds,
                WrongMoves = request.WrongMoves,
                UsedHint = request.UsedHint,
                Score = score,
                CountryCode = countryCode,
                PlayedOn = today,
                SubmittedAtUtc = DateTime.UtcNow
            };

            await using var transaction =
                await _context.Database.BeginTransactionAsync(
                    cancellationToken);

            try
            {
                _context.PuzzleAttempts.Add(attempt);
                await _context.SaveChangesAsync(cancellationToken);

                if (userId.HasValue)
                {
                    await CreateLeaderboardOutboxEntriesAsync(
                        attempt.Id,
                        userId,
                        clientId,
                        leaderboardIdentity!,
                        today,
                        countryCode,
                        cancellationToken);

                    await _context.SaveChangesAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                await transaction.RollbackAsync(cancellationToken);

                var concurrentQuery = _context.PuzzleAttempts
                    .AsNoTracking()
                    .Where(a => a.PuzzleId == request.PuzzleId);

                concurrentQuery = userId.HasValue
                    ? concurrentQuery.Where(a => a.UserId == userId.Value)
                    : concurrentQuery.Where(a =>
                        a.UserId == null && a.ClientId == clientId);

                var concurrentAttempt = await concurrentQuery
                    .FirstOrDefaultAsync(cancellationToken);

                if (concurrentAttempt != null)
                {
                    return CreateSubmitResponse(
                        concurrentAttempt,
                        "Bu bulmacayı daha önce çözdün. Önceki puanın gösteriliyor.");
                }

                throw;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }

            _logger.LogInformation(
                "Puzzle submission saved. PuzzleId: {PuzzleId}, UserId: {UserId}, Score: {Score}",
                puzzle.Id,
                userId,
                score);

            return new PuzzleSubmitResponse
            {
                Score = score,
                Seconds = request.Seconds,
                WrongMoves = request.WrongMoves,
                UsedHint = request.UsedHint,
                Message = "Puzzle result saved successfully."
            };
        }

        // ---------------------------------------------------------
        // LEADERBOARD OUTBOX
        // ---------------------------------------------------------

        private async Task CreateLeaderboardOutboxEntriesAsync(
            int puzzleAttemptId,
            int? userId,
            string? clientId,
            string leaderboardIdentity,
            DateOnly today,
            string? countryCode,
            CancellationToken cancellationToken)
        {
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
                    clientId,
                    leaderboardIdentity,
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
                        clientId,
                        leaderboardIdentity,
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
            int? userId,
            string? clientId,
            string leaderboardIdentity,
            string? countryCode,
            CancellationToken cancellationToken)
        {
            var query = _context.PuzzleAttempts
                .AsNoTracking()
                .Where(a =>
                    a.PlayedOn >= startDate &&
                    a.PlayedOn <= today);

            if (userId.HasValue)
            {
                query = query.Where(a => a.UserId == userId.Value);
            }
            else
            {
                query = query.Where(a =>
                    a.UserId == null &&
                    a.ClientId == clientId);
            }

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
                $"DailyChessPuzzle_leaderboard:{period}:{periodKey}:{countryCode ?? "global"}";

            var delivery = new LeaderboardCacheOutboxDelivery
            {
                Outbox = outboxEntry,
                TargetClientId = leaderboardIdentity,
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

        private static PuzzleSessionResponse ToSessionResponse(
            PuzzleGameSession session)
        {
            return new PuzzleSessionResponse
            {
                PuzzleId = session.PuzzleId,
                CurrentMoveIndex = session.CurrentMoveIndex,
                WrongMoves = session.WrongMoves,
                UsedHint = session.UsedHint,
                IsCompleted = session.IsCompleted,
                StartedAtUtc = session.StartedAtUtc
            };
        }

        private static PuzzleSessionResponse ToBonusSessionResponse(
            BonusPuzzleAssignment assignment)
        {
            return new PuzzleSessionResponse
            {
                PuzzleId = assignment.PuzzleId,
                CurrentMoveIndex = assignment.CurrentMoveIndex,
                WrongMoves = assignment.WrongMoves,
                UsedHint = assignment.UsedHint,
                IsCompleted = assignment.IsCompleted ||
                              assignment.CompletedAtUtc.HasValue,
                StartedAtUtc = assignment.StartedAtUtc ?? DateTime.UtcNow
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
            var isExpired =
                assignment.IsCompleted &&
                !assignment.CompletedAtUtc.HasValue &&
                assignment.StartedAtUtc.HasValue;

            return new BonusPuzzleResponse
            {
                IsEligible = true,
                IsCompleted = assignment.IsCompleted,
                IsExpired = isExpired,
                PuzzleId = assignment.PuzzleId,
                Fen = assignment.Puzzle.Fen,
                Moves = assignment.Puzzle.Moves,
                StartedAtUtc = assignment.StartedAtUtc,
                CompletedAtUtc = assignment.CompletedAtUtc,
                RemainingSeconds = GetBonusRemainingSeconds(assignment),
                Score = assignment.PuzzleAttempt?.Score
            };
        }


        private static PuzzleSubmitResponse CreateSubmitResponse(
            PuzzleAttempt attempt,
            string message)
        {
            return new PuzzleSubmitResponse
            {
                Score = attempt.Score,
                Seconds = attempt.Seconds,
                WrongMoves = attempt.WrongMoves,
                UsedHint = attempt.UsedHint,
                Message = message
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
