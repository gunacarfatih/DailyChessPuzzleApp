
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
        private readonly ApplicationDbContext _context;
        private readonly ILogger<PuzzleService> _logger;

        public PuzzleService(
            ApplicationDbContext context,
            ILogger<PuzzleService> logger)
        {
            _context = context;
            _logger = logger;
        }

        // Günün bulmacasını ve kullanıcının önceki sonucunu getirir.
        public async Task<PuzzleTodayResponse?> GetTodayAsync(
            int? userId,
            string? clientId,
            CancellationToken cancellationToken = default)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            var puzzle = await _context.DailyPuzzles
                .AsNoTracking()
                .Where(dp =>
                    dp.PuzzleDate == today &&
                    dp.Puzzle.IsActive)
                .Select(dp => dp.Puzzle)
                .FirstOrDefaultAsync(cancellationToken);

            if (puzzle == null)
            {
                _logger.LogWarning(
                    "No active puzzle found for today: {PuzzleDate}",
                    today);

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

            bool isSolved = previousAttempt != null;

            _logger.LogInformation(
                "Today's puzzle retrieved. PuzzleId: {PuzzleId}, UserId: {UserId}, IsSolved: {IsSolved}, PreviousScore: {PreviousScore}",
                puzzle.Id,
                userId,
                isSolved,
                previousAttempt?.Score);

            return new PuzzleTodayResponse
            {
                Id = puzzle.Id,
                Fen = puzzle.Fen,
                Moves = puzzle.Moves,
                Difficulty = GetDifficulty(puzzle.Rating),
                IsSolved = isSolved,
                PreviousScore = previousAttempt?.Score
            };
        }

        // Bulmaca sonucunu hesaplar ve PostgreSQL'e kaydeder.
        public async Task<PuzzleSubmitResponse> SubmitAsync(
            PuzzleSubmitRequest request,
            int? userId,
            CancellationToken cancellationToken = default)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            // Cihaz kimliği yalnızca gerçekten gönderilmişse kaydedilir.
            string? clientId = string.IsNullOrWhiteSpace(request.ClientId)
                ? null
                : request.ClientId.Trim();

            // Misafir kullanıcı için ClientId zorunludur.
            if (!userId.HasValue && clientId == null)
            {
                throw new ArgumentException("CLIENT_ID_REQUIRED");
            }

            // Leaderboard kimliği yalnızca kayıtlı kullanıcılar için kullanılır.
            string? leaderboardIdentity = userId.HasValue
                ? $"user:{userId.Value}"
                : null;

            // Kayıtlı kullanıcının ülke kodu hesabından alınır.
            // Misafir kullanıcının ülke kodu isteğinden alınır.
            string? countryCode;

            if (userId.HasValue)
            {
                countryCode = await _context.Users
                    .Where(x => x.Id == userId.Value)
                    .Select(x => x.CountryCode)
                    .FirstOrDefaultAsync(cancellationToken);

                if (countryCode == null)
                {
                    bool userExists = await _context.Users
                        .AnyAsync(
                            x => x.Id == userId.Value,
                            cancellationToken);

                    if (!userExists)
                    {
                        throw new UnauthorizedAccessException(
                            "USER_NOT_FOUND");
                    }
                }
            }
            else
            {
                countryCode = request.CountryCode;
            }

            countryCode = string.IsNullOrWhiteSpace(countryCode)
                ? null
                : countryCode.Trim().ToUpperInvariant();

            var puzzle = await _context.DailyPuzzles
                .Where(dp =>
                    dp.PuzzleId == request.PuzzleId &&
                    dp.PuzzleDate == today &&
                    dp.Puzzle.IsActive)
                .Select(dp => dp.Puzzle)
                .FirstOrDefaultAsync(cancellationToken);

            if (puzzle == null)
            {
                _logger.LogWarning(
                    "Puzzle submission failed. Puzzle not found. PuzzleId: {PuzzleId}",
                    request.PuzzleId);

                throw new KeyNotFoundException(
                    "Today's puzzle was not found.");
            }

            // Kayıtlı kullanıcı UserId üzerinden,
            // misafir kullanıcı ClientId üzerinden kontrol edilir.
            var previousAttemptQuery = _context.PuzzleAttempts
                .AsNoTracking()
                .Where(a => a.PuzzleId == request.PuzzleId);

            if (userId.HasValue)
            {
                previousAttemptQuery = previousAttemptQuery.Where(
                    a => a.UserId == userId.Value);
            }
            else
            {
                previousAttemptQuery = previousAttemptQuery.Where(
                    a => a.UserId == null &&
                         a.ClientId == clientId);
            }

            var previousAttempt = await previousAttemptQuery
                .FirstOrDefaultAsync(cancellationToken);

            if (previousAttempt != null)
            {
                _logger.LogInformation(
                    "Previous puzzle result returned. PuzzleId: {PuzzleId}, UserId: {UserId}, Score: {Score}",
                    request.PuzzleId,
                    userId,
                    previousAttempt.Score);

                return CreateSubmitResponse(
                    previousAttempt,
                    "Bu bulmacayı daha önce çözdün. Önceki puanın gösteriliyor.");
            }

            // Puan hesaplaması.
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
                // 1. Bulmaca denemesini kaydet.
                await _context.PuzzleAttempts.AddAsync(
                    attempt,
                    cancellationToken);

                await _context.SaveChangesAsync(cancellationToken);

                // 2. Yalnızca kayıtlı kullanıcı için leaderboard
                // Outbox ve Delivery kayıtlarını oluştur.
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

                // 3. Transaction tamamla.
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (
                ex.InnerException is PostgresException postgresException &&
                postgresException.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                await transaction.RollbackAsync(cancellationToken);

                // Aynı anda gelen isteklerden diğeri önce kayıt yapmış olabilir.
                var concurrentAttemptQuery = _context.PuzzleAttempts
                    .AsNoTracking()
                    .Where(a => a.PuzzleId == request.PuzzleId);

                if (userId.HasValue)
                {
                    concurrentAttemptQuery = concurrentAttemptQuery.Where(
                        a => a.UserId == userId.Value);
                }
                else
                {
                    concurrentAttemptQuery = concurrentAttemptQuery.Where(
                        a => a.UserId == null &&
                             a.ClientId == clientId);
                }

                var concurrentAttempt = await concurrentAttemptQuery
                    .FirstOrDefaultAsync(cancellationToken);

                if (concurrentAttempt != null)
                {
                    _logger.LogInformation(
                        "Concurrent duplicate submission handled. PuzzleId: {PuzzleId}, UserId: {UserId}, Score: {Score}",
                        request.PuzzleId,
                        userId,
                        concurrentAttempt.Score);

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

            if (userId.HasValue)
            {
                _logger.LogInformation(
                    "Puzzle submission and leaderboard outbox event saved successfully. PuzzleId: {PuzzleId}, UserId: {UserId}, Score: {Score}",
                    puzzle.Id,
                    userId,
                    score);
            }
            else
            {
                _logger.LogInformation(
                    "Guest puzzle submission saved without leaderboard entry. PuzzleId: {PuzzleId}, ClientId: {ClientId}, Score: {Score}",
                    puzzle.Id,
                    clientId,
                    score);
            }

            return new PuzzleSubmitResponse
            {
                Score = score,
                Seconds = request.Seconds,
                WrongMoves = request.WrongMoves,
                UsedHint = request.UsedHint,
                Message = "Puzzle result saved successfully."
            };
        }

        // Mevcut denemeden sonuç DTO'su oluşturur.
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

        // Tek bir ana Outbox olayı oluşturur.
        // Günlük, haftalık, aylık ve tüm zamanlar Delivery kayıtlarını hazırlar.
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

            await _context.LeaderboardCacheOutboxes.AddAsync(
                outboxEntry,
                cancellationToken);

            // Günlük dönem.
            var dailyPeriod = (
                Period: "daily",
                PeriodKey: today.ToString("yyyy-MM-dd"),
                StartDate: today);

            // Haftalık dönem: ISO standardına göre pazartesi başlangıcı.
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

            // Aylık dönem.
            var monthlyStartDate = new DateOnly(
                today.Year,
                today.Month,
                1);

            var monthlyPeriod = (
                Period: "monthly",
                PeriodKey: today.ToString("yyyy-MM"),
                StartDate: monthlyStartDate);

            // Tüm zamanlar dönemi.
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
                // Global leaderboard Delivery kaydı.
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

                // Ülke bazlı leaderboard Delivery kaydı.
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

            _logger.LogInformation(
                "Leaderboard outbox event created. OutboxId: {OutboxId}, PuzzleAttemptId: {PuzzleAttemptId}, LeaderboardIdentity: {LeaderboardIdentity}",
                outboxEntry.Id,
                puzzleAttemptId,
                leaderboardIdentity);
        }

        // Belirli bir Redis hedefi için Delivery kaydı oluşturur.
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

            // Kayıtlı kullanıcı bütün cihazlardaki denemeleriyle hesaplanır.
            if (userId.HasValue)
            {
                query = query.Where(
                    a => a.UserId == userId.Value);
            }
            else
            {
                query = query.Where(
                    a => a.UserId == null &&
                         a.ClientId == clientId);
            }

            if (countryCode != null)
            {
                query = query.Where(
                    a => a.CountryCode == countryCode);
            }

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

            await _context.LeaderboardCacheOutboxDeliveries.AddAsync(
                delivery,
                cancellationToken);

            _logger.LogInformation(
                "Leaderboard delivery created. Period: {Period}, PeriodKey: {PeriodKey}, Identity: {Identity}, Country: {Country}",
                period,
                periodKey,
                leaderboardIdentity,
                countryCode ?? "Global");
        }

        // Rating değerinden zorluk seviyesini belirler.
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
