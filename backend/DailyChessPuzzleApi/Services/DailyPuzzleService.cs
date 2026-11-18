using DailyChessPuzzleApi.Data;
using DailyChessPuzzleApi.Models;
using DailyChessPuzzleApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DailyChessPuzzleApi.Services
{
    public class DailyPuzzleService : IDailyPuzzleService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<DailyPuzzleService> _logger;

        public DailyPuzzleService(
            ApplicationDbContext context,
            ILogger<DailyPuzzleService> logger)
        {
            _context = context;
            _logger = logger;
        }

        // Belirtilen tarih için Guest ve Registered bulmacalarını atar.
        public async Task AssignDailyPuzzleAsync(
            DateOnly puzzleDate,
            CancellationToken cancellationToken = default)
        {
            await AssignPuzzleForAudienceAsync(
                puzzleDate,
                PuzzleAudience.Guest,
                cancellationToken);

            await AssignPuzzleForAudienceAsync(
                puzzleDate,
                PuzzleAudience.Registered,
                cancellationToken);
        }

        // Belirli bir kullanıcı grubu için günlük bulmaca atar.
        private async Task AssignPuzzleForAudienceAsync(
            DateOnly puzzleDate,
            PuzzleAudience audience,
            CancellationToken cancellationToken)
        {
            // Bu kullanıcı grubuna bugün zaten bulmaca atanmış mı?
            var alreadyAssigned = await _context.DailyPuzzles
                .AnyAsync(
                    dp =>
                        dp.PuzzleDate == puzzleDate &&
                        dp.Audience == audience,
                    cancellationToken);

            if (alreadyAssigned)
            {
                _logger.LogInformation(
                    "Daily puzzle already assigned. Date: {PuzzleDate}, Audience: {Audience}",
                    puzzleDate,
                    audience);

                return;
            }

            // Daha önce günlük bulmaca olarak kullanılmamış
            // aktif bir bulmaca seç.
            // Aynı günün diğer kullanıcı grubuna atanmış bulmaca da
            // kullanılabilir.
            var puzzle = await _context.Puzzles
                .Where(p =>
                    p.IsActive &&
                    !_context.DailyPuzzles.Any(
                        dp => dp.PuzzleId == p.Id))
                .OrderBy(p => EF.Functions.Random())
                .FirstOrDefaultAsync(cancellationToken);

            if (puzzle == null)
            {
                _logger.LogWarning(
                    "No available active puzzle found. Date: {PuzzleDate}, Audience: {Audience}",
                    puzzleDate,
                    audience);

                throw new InvalidOperationException(
                    "No available puzzles found.");
            }

            var dailyPuzzle = new DailyPuzzle
            {
                PuzzleId = puzzle.Id,
                PuzzleDate = puzzleDate,
                Audience = audience
            };

            await _context.DailyPuzzles.AddAsync(
                dailyPuzzle,
                cancellationToken);

            try
            {
                await _context.SaveChangesAsync(cancellationToken);

                _logger.LogInformation(
                    "Daily puzzle assigned successfully. PuzzleId: {PuzzleId}, Date: {PuzzleDate}, Audience: {Audience}",
                    puzzle.Id,
                    puzzleDate,
                    audience);
            }
            catch (DbUpdateException ex) when (
                ex.InnerException is PostgresException postgresException &&
                postgresException.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                // Eş zamanlı başka bir işlem aynı gruba atama yapmış olabilir.
                _context.Entry(dailyPuzzle).State = EntityState.Detached;

                var assignmentExists = await _context.DailyPuzzles
                    .AnyAsync(
                        dp =>
                            dp.PuzzleDate == puzzleDate &&
                            dp.Audience == audience,
                        cancellationToken);

                if (assignmentExists)
                {
                    _logger.LogInformation(
                        "Daily puzzle was assigned by another request. Date: {PuzzleDate}, Audience: {Audience}",
                        puzzleDate,
                        audience);

                    return;
                }

                throw;
            }
        }
    }
}
