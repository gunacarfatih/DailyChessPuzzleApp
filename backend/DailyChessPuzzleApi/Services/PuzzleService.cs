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
            string clientId,
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
                    today
                );

                return null;
            }

            // Kullanıcının bu bulmaca için daha önce kaydettiği denemeyi bul.
            var previousAttempt = await _context.PuzzleAttempts
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    a => a.PuzzleId == puzzle.Id &&
                         a.ClientId == clientId,
                    cancellationToken
                );

            bool isSolved = previousAttempt != null;

            _logger.LogInformation(
                "Today's puzzle retrieved. PuzzleId: {PuzzleId}, IsSolved: {IsSolved}, PreviousScore: {PreviousScore}",
                puzzle.Id,
                isSolved,
                previousAttempt?.Score
            );

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
        // Daha önce kayıt varsa mevcut sonucu tekrar döndürür.
        public async Task<PuzzleSubmitResponse> SubmitAsync(
            PuzzleSubmitRequest request,
            CancellationToken cancellationToken = default)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

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
                    request.PuzzleId
                );

                throw new KeyNotFoundException(
                    "Today's puzzle was not found."
                );
            }

            // Önceden kayıtlı sonuç varsa yeni kayıt oluşturma.
            var previousAttempt = await _context.PuzzleAttempts
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    a => a.PuzzleId == request.PuzzleId &&
                         a.ClientId == request.ClientId,
                    cancellationToken
                );

            if (previousAttempt != null)
            {
                _logger.LogInformation(
                    "Previous puzzle result returned. PuzzleId: {PuzzleId}, Score: {Score}",
                    request.PuzzleId,
                    previousAttempt.Score
                );

                return CreateSubmitResponse(
                    previousAttempt,
                    "Bu bulmacayı daha önce çözdün. Önceki puanın gösteriliyor."
                );
            }

            // Puan hesaplaması.
            long timePenalty = (long)request.Seconds * 2;
            long wrongMovePenalty = (long)request.WrongMoves * 150;
            long hintPenalty = request.UsedHint ? 250 : 0;

            int score = (int)Math.Max(
                0L,
                1000L - timePenalty - wrongMovePenalty - hintPenalty
            );

            string? countryCode = string.IsNullOrWhiteSpace(request.CountryCode)
                ? null
                : request.CountryCode.Trim().ToUpperInvariant();

            var attempt = new PuzzleAttempt
            {
                PuzzleId = puzzle.Id,
                ClientId = request.ClientId,
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
                    cancellationToken
                );

            try
            {
                // 1. Bulmaca denemesini kaydet.
                await _context.PuzzleAttempts.AddAsync(
                    attempt,
                    cancellationToken
                );

                await _context.SaveChangesAsync(cancellationToken);

                // 2. Leaderboard Outbox ve Delivery kayıtlarını oluştur.
                await CreateLeaderboardOutboxEntriesAsync(
                    attempt.Id,
                    request.ClientId,
                    today,
                    countryCode,
                    cancellationToken
                );

                // 3. Outbox ve Delivery kayıtlarını kaydet.
                await _context.SaveChangesAsync(cancellationToken);

                // 4. Transaction tamamla.
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (
                ex.InnerException is PostgresException postgresException &&
                postgresException.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                await transaction.RollbackAsync(cancellationToken);

                // Aynı anda gelen iki istekte diğer istek önce kaydetmiş olabilir.
                // Bu durumda onun kaydını bulup mevcut puanı döndür.
                var concurrentAttempt = await _context.PuzzleAttempts
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        a => a.PuzzleId == request.PuzzleId &&
                             a.ClientId == request.ClientId,
                        cancellationToken
                    );

                if (concurrentAttempt != null)
                {
                    _logger.LogInformation(
                        "Concurrent duplicate submission handled. PuzzleId: {PuzzleId}, Score: {Score}",
                        request.PuzzleId,
                        concurrentAttempt.Score
                    );

                    return CreateSubmitResponse(
                        concurrentAttempt,
                        "Bu bulmacayı daha önce çözdün. Önceki puanın gösteriliyor."
                    );
                }

                // Unique ihlali başka bir tabloda oluştuysa gerçek hatayı koru.
                throw;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }

            _logger.LogInformation(
                "Puzzle submission and leaderboard outbox event saved successfully. PuzzleId: {PuzzleId}, Score: {Score}",
                puzzle.Id,
                score
            );

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
        // Ardından günlük, haftalık, aylık ve tüm zamanlar
        // için gerekli Delivery kayıtlarını oluşturur.
        private async Task CreateLeaderboardOutboxEntriesAsync(
            int puzzleAttemptId,
            string clientId,
            DateOnly today,
            string? countryCode,
            CancellationToken cancellationToken)
        {
            var outboxEntry = new LeaderboardCacheOutbox
            {
                PuzzleAttemptId = puzzleAttemptId,
                ClientId = clientId,
                CreatedAtUtc = DateTime.UtcNow
            };

            await _context.LeaderboardCacheOutbox.AddAsync(
                outboxEntry,
                cancellationToken
            );

            // Günlük dönem.
            var dailyPeriod = (
                Period: "daily",
                PeriodKey: today.ToString("yyyy-MM-dd"),
                StartDate: today
            );

            // Haftalık dönem: ISO standardına göre pazartesi başlangıcı.
            int daysSinceMonday =
                ((int)today.DayOfWeek + 6) % 7;

            var weeklyStartDate = today.AddDays(-daysSinceMonday);

            int isoYear = ISOWeek.GetYear(
                today.ToDateTime(TimeOnly.MinValue)
            );

            int isoWeek = ISOWeek.GetWeekOfYear(
                today.ToDateTime(TimeOnly.MinValue)
            );

            var weeklyPeriod = (
                Period: "weekly",
                PeriodKey: $"{isoYear}-W{isoWeek:D2}",
                StartDate: weeklyStartDate
            );

            // Aylık dönem.
            var monthlyStartDate = new DateOnly(
                today.Year,
                today.Month,
                1
            );

            var monthlyPeriod = (
                Period: "monthly",
                PeriodKey: today.ToString("yyyy-MM"),
                StartDate: monthlyStartDate
            );

            // Tüm zamanlar dönemi.
            var allTimePeriod = (
                Period: "alltime",
                PeriodKey: "all",
                StartDate: DateOnly.MinValue
            );

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
                    clientId,
                    null,
                    cancellationToken
                );

                // Ülke bazlı leaderboard Delivery kaydı.
                if (countryCode != null)
                {
                    await CreateLeaderboardOutboxDeliveryAsync(
                        outboxEntry,
                        period.Period,
                        period.PeriodKey,
                        period.StartDate,
                        today,
                        clientId,
                        countryCode,
                        cancellationToken
                    );
                }
            }

            _logger.LogInformation(
                "Leaderboard outbox event created. OutboxId: {OutboxId}, PuzzleAttemptId: {PuzzleAttemptId}, ClientId: {ClientId}",
                outboxEntry.Id,
                puzzleAttemptId,
                clientId
            );
        }

        // Belirli bir Redis hedefi için Delivery kaydı oluşturur.
        private async Task CreateLeaderboardOutboxDeliveryAsync(
            LeaderboardCacheOutbox outboxEntry,
            string period,
            string periodKey,
            DateOnly startDate,
            DateOnly today,
            string clientId,
            string? countryCode,
            CancellationToken cancellationToken)
        {
            var query = _context.PuzzleAttempts
                .AsNoTracking()
                .Where(a =>
                    a.ClientId == clientId &&
                    a.PlayedOn >= startDate &&
                    a.PlayedOn <= today
                );

            if (countryCode != null)
            {
                query = query.Where(
                    a => a.CountryCode == countryCode
                );
            }

            var aggregate = await query
                .GroupBy(a => a.ClientId)
                .Select(group => new
                {
                    TotalScore = group.Sum(a => a.Score),
                    SolvedCount = group.Count()
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (aggregate == null)
            {
                return;
            }

            string cacheKey =
                $"DailyChessPuzzle_leaderboard:{period}:{periodKey}:{countryCode ?? "global"}";

            var delivery = new LeaderboardCacheOutboxDelivery
            {
                Outbox = outboxEntry,
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
                cancellationToken
            );

            _logger.LogInformation(
                "Leaderboard delivery created. Period: {Period}, PeriodKey: {PeriodKey}, ClientId: {ClientId}, Country: {Country}",
                period,
                periodKey,
                clientId,
                countryCode ?? "Global"
            );
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