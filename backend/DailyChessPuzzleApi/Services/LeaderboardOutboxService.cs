
using DailyChessPuzzleApi.Data;
using DailyChessPuzzleApi.Models;
using DailyChessPuzzleApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DailyChessPuzzleApi.Services
{
    public class LeaderboardOutboxService : ILeaderboardOutboxService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILeaderboardCacheService _cacheService;
        private readonly ILogger<LeaderboardOutboxService> _logger;

        private static readonly TimeSpan LockDuration =
            TimeSpan.FromMinutes(5);

        public LeaderboardOutboxService(
            ApplicationDbContext context,
            ILeaderboardCacheService cacheService,
            ILogger<LeaderboardOutboxService> logger)
        {
            _context = context;
            _cacheService = cacheService;
            _logger = logger;
        }

        public async Task ProcessPendingAsync(
            CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;

            var deliveryIds = await _context.LeaderboardCacheOutboxDeliveries
                .AsNoTracking()
                .Where(x =>
                    x.Status == LeaderboardCacheOutboxDeliveryStatus.Pending ||
                    (
                        x.Status == LeaderboardCacheOutboxDeliveryStatus.Failed &&
                        x.NextRetryAtUtc != null &&
                        x.NextRetryAtUtc <= now
                    ) ||
                    (
                        x.Status == LeaderboardCacheOutboxDeliveryStatus.Processing &&
                        x.LockedUntilUtc != null &&
                        x.LockedUntilUtc <= now
                    ))
                .OrderBy(x => x.CreatedAtUtc)
                .Select(x => x.Id)
                .Take(100)
                .ToListAsync(cancellationToken);

            foreach (var deliveryId in deliveryIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string lockId = Guid.NewGuid().ToString("N");

                bool lockAcquired = await TryAcquireLockAsync(
                    deliveryId,
                    lockId,
                    cancellationToken);

                if (!lockAcquired)
                    continue;

                int retryCount = 0;

                try
                {
                    await using var transaction =
                        await _context.Database.BeginTransactionAsync(
                            cancellationToken);

                    var delivery = await _context
                        .LeaderboardCacheOutboxDeliveries
                        .AsNoTracking()
                        .Include(x => x.Outbox)
                        .FirstOrDefaultAsync(
                            x => x.Id == deliveryId,
                            cancellationToken);

                    if (delivery == null)
                    {
                        await transaction.CommitAsync(cancellationToken);
                        continue;
                    }

                    retryCount = delivery.RetryCount + 1;

                    if (delivery.Status !=
                            LeaderboardCacheOutboxDeliveryStatus.Processing ||
                        delivery.LockedBy != lockId)
                    {
                        await transaction.CommitAsync(cancellationToken);
                        continue;
                    }

                    string targetClientId = delivery.TargetClientId;

                    PuzzleAttempt? puzzleAttempt = null;

                    // PuzzleAttemptId null ise bu bir toplu işlem
                    // (hesap birleştirme gibi) Outbox kaydıdır.
                    if (delivery.Outbox.PuzzleAttemptId.HasValue)
                    {
                        puzzleAttempt = await _context.PuzzleAttempts
                            .FromSqlInterpolated(
                                $"""
                                SELECT *
                                FROM "PuzzleAttempts"
                                WHERE "Id" = {delivery.Outbox.PuzzleAttemptId.Value}
                                FOR UPDATE
                                """)
                            .SingleOrDefaultAsync(cancellationToken);
                    }

                    // Normal puan güncellemesinde attempt hâlâ
                    // aynı kullanıcıya ait olmalıdır.
                    if (!delivery.IsRemoval)
                    {
                        if (delivery.Outbox.PuzzleAttemptId.HasValue &&
                            (puzzleAttempt == null ||
                             !IsIdentityCurrent(targetClientId, puzzleAttempt)))
                        {
                            await CompleteDeliveryAsync(
                                deliveryId,
                                lockId,
                                cancellationToken);

                            await transaction.CommitAsync(cancellationToken);

                            _logger.LogInformation(
                                "Obsolete leaderboard update skipped. " +
                                "DeliveryId: {DeliveryId}, TargetClientId: {ClientId}",
                                deliveryId,
                                targetClientId);

                            _context.ChangeTracker.Clear();
                            continue;
                        }
                    }
                    else
                    {
                        // Silme teslimatı ancak attempt artık eski
                        // kimliğe ait değilse uygulanabilir.
                        if (puzzleAttempt != null &&
                            IsIdentityCurrent(targetClientId, puzzleAttempt))
                        {
                            await CompleteDeliveryAsync(
                                deliveryId,
                                lockId,
                                cancellationToken);

                            await transaction.CommitAsync(cancellationToken);

                            _logger.LogInformation(
                                "Obsolete leaderboard removal skipped. " +
                                "DeliveryId: {DeliveryId}, TargetClientId: {ClientId}",
                                deliveryId,
                                targetClientId);

                            _context.ChangeTracker.Clear();
                            continue;
                        }
                    }

                    // Mevcut revision sistemi korunuyor.
                    // Her Outbox kaydının revision değeri kendi Id değeridir.
                    long incomingRevision = delivery.OutboxId;

                    var existingScore = await _context.LeaderboardScores
                        .AsNoTracking()
                        .FirstOrDefaultAsync(x =>
                            x.ClientId == targetClientId &&
                            x.Period == delivery.Period &&
                            x.PeriodKey == delivery.PeriodKey &&
                            x.CountryCode == delivery.CountryCode,
                            cancellationToken);

                    bool shouldRemoveFromRedis = false;

                    if (delivery.IsRemoval)
                    {
                        if (existingScore == null)
                        {
                            // Silme revision'ını PostgreSQL'de de tut.
                            var tombstoneScore = new LeaderboardScore
                            {
                                ClientId = targetClientId,
                                Period = delivery.Period,
                                PeriodKey = delivery.PeriodKey,
                                CountryCode = delivery.CountryCode,
                                TotalScore = 0,
                                SolvedCount = 0,
                                Revision = incomingRevision,
                                UpdatedAtUtc = DateTime.UtcNow
                            };

                            await _context.LeaderboardScores.AddAsync(
                                tombstoneScore,
                                cancellationToken);

                            await _context.SaveChangesAsync(cancellationToken);

                            shouldRemoveFromRedis = true;
                        }
                        else if (incomingRevision > existingScore.Revision)
                        {
                            await _context.LeaderboardScores
                                .Where(x =>
                                    x.Id == existingScore.Id &&
                                    x.Revision < incomingRevision)
                                .ExecuteUpdateAsync(setters => setters
                                    .SetProperty(x => x.TotalScore, 0)
                                    .SetProperty(x => x.SolvedCount, 0)
                                    .SetProperty(x => x.Revision, incomingRevision)
                                    .SetProperty(x => x.UpdatedAtUtc, DateTime.UtcNow),
                                    cancellationToken);

                            shouldRemoveFromRedis = true;
                        }
                    }
                    else
                    {
                        if (existingScore == null)
                        {
                            var newScore = new LeaderboardScore
                            {
                                ClientId = targetClientId,
                                Period = delivery.Period,
                                PeriodKey = delivery.PeriodKey,
                                CountryCode = delivery.CountryCode,
                                TotalScore = delivery.TotalScore,
                                SolvedCount = delivery.SolvedCount,
                                Revision = incomingRevision,
                                UpdatedAtUtc = DateTime.UtcNow
                            };

                            await _context.LeaderboardScores.AddAsync(
                                newScore,
                                cancellationToken);

                            await _context.SaveChangesAsync(cancellationToken);
                        }
                        else if (incomingRevision > existingScore.Revision)
                        {
                            await _context.LeaderboardScores
                                .Where(x =>
                                    x.Id == existingScore.Id &&
                                    x.Revision < incomingRevision)
                                .ExecuteUpdateAsync(setters => setters
                                    .SetProperty(
                                        x => x.TotalScore,
                                        delivery.TotalScore)
                                    .SetProperty(
                                        x => x.SolvedCount,
                                        delivery.SolvedCount)
                                    .SetProperty(
                                        x => x.Revision,
                                        incomingRevision)
                                    .SetProperty(
                                        x => x.UpdatedAtUtc,
                                        DateTime.UtcNow),
                                    cancellationToken);
                        }
                    }

                    var currentScore = await _context.LeaderboardScores
                        .AsNoTracking()
                        .FirstOrDefaultAsync(x =>
                            x.ClientId == targetClientId &&
                            x.Period == delivery.Period &&
                            x.PeriodKey == delivery.PeriodKey &&
                            x.CountryCode == delivery.CountryCode,
                            cancellationToken);

                    if (currentScore == null)
                    {
                        throw new InvalidOperationException(
                            "Leaderboard score could not be loaded after update.");
                    }

                    if (delivery.IsRemoval)
                    {
                        // Daha yeni bir skor mevcutsa eski silme
                        // teslimatı bu skoru kaldıramaz.
                        if (shouldRemoveFromRedis)
                        {
                            if (delivery.Period.Equals(
                                "daily",
                                StringComparison.OrdinalIgnoreCase))
                            {
                                string versionKey =
                                    $"{delivery.CacheKey}:version";

                                await _cacheService.RemoveDailyScoreAsync(
                                    delivery.CacheKey,
                                    versionKey,
                                    targetClientId,
                                    incomingRevision);
                            }
                            else
                            {
                                await _cacheService.RemoveScoreAsync(
                                    delivery.CacheKey,
                                    targetClientId,
                                    incomingRevision);
                            }
                        }
                    }
                    else
                    {
                        if (delivery.Period.Equals(
                            "daily",
                            StringComparison.OrdinalIgnoreCase))
                        {
                            string versionKey =
                                $"{delivery.CacheKey}:version";

                            await _cacheService.UpdateDailyScoreAsync(
                                delivery.CacheKey,
                                versionKey,
                                targetClientId,
                                currentScore.TotalScore,
                                currentScore.SolvedCount,
                                currentScore.Revision);
                        }
                        else
                        {
                            await _cacheService.UpdateScoreAsync(
                                delivery.CacheKey,
                                targetClientId,
                                currentScore.TotalScore,
                                currentScore.SolvedCount,
                                currentScore.Revision);
                        }
                    }

                    int completedRows = await CompleteDeliveryAsync(
                        deliveryId,
                        lockId,
                        cancellationToken);

                    if (completedRows == 0)
                    {
                        _logger.LogWarning(
                            "Delivery completion skipped because the lock " +
                            "is no longer owned by this worker. DeliveryId: {DeliveryId}",
                            deliveryId);

                        await transaction.RollbackAsync(cancellationToken);
                        continue;
                    }

                    await transaction.CommitAsync(cancellationToken);

                    _logger.LogInformation(
                        "Leaderboard Delivery completed. " +
                        "DeliveryId: {DeliveryId}, Period: {Period}, " +
                        "TargetClientId: {ClientId}, IsRemoval: {IsRemoval}, " +
                        "Revision: {Revision}",
                        delivery.Id,
                        delivery.Period,
                        targetClientId,
                        delivery.IsRemoval,
                        currentScore.Revision);

                    _context.ChangeTracker.Clear();
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _context.ChangeTracker.Clear();

                    int actualRetryCount = Math.Max(1, retryCount);

                    int retryDelayMinutes = Math.Min(
                        (int)Math.Pow(2, actualRetryCount),
                        30);

                    string errorMessage = ex.Message.Length > 2000
                        ? ex.Message[..2000]
                        : ex.Message;

                    var nextRetryAt = DateTime.UtcNow.AddMinutes(
                        retryDelayMinutes);

                    try
                    {
                        await _context
                            .LeaderboardCacheOutboxDeliveries
                            .Where(x =>
                                x.Id == deliveryId &&
                                x.Status ==
                                    LeaderboardCacheOutboxDeliveryStatus.Processing &&
                                x.LockedBy == lockId)
                            .ExecuteUpdateAsync(setters => setters
                                .SetProperty(
                                    x => x.Status,
                                    LeaderboardCacheOutboxDeliveryStatus.Failed)
                                .SetProperty(
                                    x => x.RetryCount,
                                    actualRetryCount)
                                .SetProperty(
                                    x => x.LastError,
                                    errorMessage)
                                .SetProperty(
                                    x => x.NextRetryAtUtc,
                                    nextRetryAt)
                                .SetProperty(
                                    x => x.LockedUntilUtc,
                                    (DateTime?)null)
                                .SetProperty(
                                    x => x.LockedBy,
                                    (string?)null),
                                cancellationToken);
                    }
                    catch (Exception saveException)
                    {
                        _logger.LogError(
                            saveException,
                            "Delivery failure state could not be saved. " +
                            "DeliveryId: {DeliveryId}",
                            deliveryId);
                    }

                    _logger.LogError(
                        ex,
                        "Leaderboard Delivery failed. " +
                        "DeliveryId: {DeliveryId}, RetryCount: {RetryCount}",
                        deliveryId,
                        actualRetryCount);
                }
            }
        }

        private static bool IsIdentityCurrent(
            string targetClientId,
            PuzzleAttempt attempt)
        {
            if (targetClientId.StartsWith(
                "user:",
                StringComparison.Ordinal))
            {
                string userIdText =
                    targetClientId["user:".Length..];

                return int.TryParse(userIdText, out int userId) &&
                       attempt.UserId == userId;
            }

            return attempt.UserId == null &&
                   attempt.ClientId != null &&
                   string.Equals(
                       attempt.ClientId,
                       targetClientId,
                       StringComparison.Ordinal);
        }

        private async Task<int> CompleteDeliveryAsync(
            int deliveryId,
            string lockId,
            CancellationToken cancellationToken)
        {
            var processedAt = DateTime.UtcNow;

            return await _context
                .LeaderboardCacheOutboxDeliveries
                .Where(x =>
                    x.Id == deliveryId &&
                    x.Status ==
                        LeaderboardCacheOutboxDeliveryStatus.Processing &&
                    x.LockedBy == lockId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(
                        x => x.Status,
                        LeaderboardCacheOutboxDeliveryStatus.Completed)
                    .SetProperty(
                        x => x.ProcessedAtUtc,
                        processedAt)
                    .SetProperty(
                        x => x.NextRetryAtUtc,
                        (DateTime?)null)
                    .SetProperty(
                        x => x.LockedUntilUtc,
                        (DateTime?)null)
                    .SetProperty(
                        x => x.LockedBy,
                        (string?)null)
                    .SetProperty(
                        x => x.LastError,
                        (string?)null),
                    cancellationToken);
        }

        private async Task<bool> TryAcquireLockAsync(
            int deliveryId,
            string lockId,
            CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;
            var lockUntil = now.Add(LockDuration);

            int affectedRows = await _context
                .LeaderboardCacheOutboxDeliveries
                .Where(x =>
                    x.Id == deliveryId &&
                    (
                        x.Status ==
                            LeaderboardCacheOutboxDeliveryStatus.Pending ||
                        (
                            x.Status ==
                                LeaderboardCacheOutboxDeliveryStatus.Failed &&
                            x.NextRetryAtUtc != null &&
                            x.NextRetryAtUtc <= now
                        ) ||
                        (
                            x.Status ==
                                LeaderboardCacheOutboxDeliveryStatus.Processing &&
                            x.LockedUntilUtc != null &&
                            x.LockedUntilUtc <= now
                        )
                    ))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(
                        x => x.Status,
                        LeaderboardCacheOutboxDeliveryStatus.Processing)
                    .SetProperty(
                        x => x.LockedBy,
                        lockId)
                    .SetProperty(
                        x => x.LockedUntilUtc,
                        lockUntil)
                    .SetProperty(
                        x => x.LastError,
                        (string?)null),
                    cancellationToken);

            return affectedRows == 1;
        }
    }
}
