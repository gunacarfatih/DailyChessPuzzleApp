
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
        private readonly ILeaderboardPublisher _publisher;

        private static readonly TimeSpan LockDuration =
            TimeSpan.FromMinutes(5);

        public LeaderboardOutboxService(
            ApplicationDbContext context,
            ILeaderboardCacheService cacheService,
            ILogger<LeaderboardOutboxService> logger,
            ILeaderboardPublisher publisher)
        {
            _context = context;
            _cacheService = cacheService;
            _logger = logger;
            _publisher = publisher;
        }

        public async Task<bool> ProcessPendingAsync(
            CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;
            var batchLockId = Guid.NewGuid().ToString("N");
            var processingStatus = (int)LeaderboardCacheOutboxDeliveryStatus.Processing;
            var pendingStatus = (int)LeaderboardCacheOutboxDeliveryStatus.Pending;
            var failedStatus = (int)LeaderboardCacheOutboxDeliveryStatus.Failed;
            var lockUntil = now.Add(LockDuration);
            List<int> deliveryIds;

            await using (var claimTransaction = await _context.Database
                .BeginTransactionAsync(cancellationToken))
            {
                var candidates = await _context.LeaderboardCacheOutboxDeliveries
                    .FromSqlInterpolated($"""
                        SELECT * FROM "LeaderboardCacheOutboxDeliveries"
                        WHERE "Status" = {pendingStatus}
                           OR ("Status" = {failedStatus}
                               AND "NextRetryAtUtc" IS NOT NULL
                               AND "NextRetryAtUtc" <= {now})
                           OR ("Status" = {processingStatus}
                               AND "LockedUntilUtc" IS NOT NULL
                               AND "LockedUntilUtc" <= {now})
                        ORDER BY "CreatedAtUtc"
                        FOR UPDATE SKIP LOCKED
                        LIMIT 100
                        """)
                    .AsNoTracking()
                    .ToListAsync(cancellationToken);
                deliveryIds = candidates.Select(x => x.Id).ToList();

                if (deliveryIds.Count > 0)
                {
                    await _context.LeaderboardCacheOutboxDeliveries
                        .Where(x => deliveryIds.Contains(x.Id))
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(x => x.Status,
                                LeaderboardCacheOutboxDeliveryStatus.Processing)
                            .SetProperty(x => x.LockedBy, batchLockId)
                            .SetProperty(x => x.LockedUntilUtc, lockUntil)
                            .SetProperty(x => x.LastError, (string?)null),
                            cancellationToken);
                }

                await claimTransaction.CommitAsync(cancellationToken);
                _context.ChangeTracker.Clear();
            }

            if (deliveryIds.Count == 0) return false;

            foreach (var deliveryId in deliveryIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string lockId = batchLockId;

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

                    // Yalnızca geçerli kayıtlı kullanıcı kimlikleri
                    // liderlik tablosuna dahil edilebilir.
                    // Misafir veya hatalı kimliklere ait teslimatlar atlanır.
                    if (!IsRegisteredIdentity(targetClientId))
                    {
                        await CompleteDeliveryAsync(
                            deliveryId,
                            lockId,
                            cancellationToken);

                        await transaction.CommitAsync(cancellationToken);

                        _logger.LogInformation(
                            "Guest or invalid leaderboard delivery skipped. " +
                            "DeliveryId: {DeliveryId}",
                            deliveryId);

                        _context.ChangeTracker.Clear();
                        continue;
                    }

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
                                "DeliveryId: {DeliveryId}",
                                deliveryId);

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
                                "DeliveryId: {DeliveryId}",
                                deliveryId);

                            _context.ChangeTracker.Clear();
                            continue;
                        }
                    }

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
                    bool leaderboardChanged = false;

                    if (delivery.IsRemoval)
                    {
                        if (existingScore == null)
                        {
                            // Silme revision'ını PostgreSQL'de de tut.
                            var tombstoneScore = new LeaderboardScore
                            {
                                ClientId = targetClientId,
                                UserId = delivery.TargetUserId,
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
                            leaderboardChanged = true;
                        }
                        else if (incomingRevision > existingScore.Revision)
                        {
                            int affectedRows = await _context.LeaderboardScores
                                .Where(x =>
                                    x.Id == existingScore.Id &&
                                    x.Revision < incomingRevision)
                                .ExecuteUpdateAsync(setters => setters
                                    .SetProperty(x => x.TotalScore, 0)
                                    .SetProperty(x => x.SolvedCount, 0)
                                    .SetProperty(x => x.Revision, incomingRevision)
                                    .SetProperty(x => x.UpdatedAtUtc, DateTime.UtcNow),
                                    cancellationToken);

                            shouldRemoveFromRedis = affectedRows > 0;
                            leaderboardChanged = affectedRows > 0;
                        }
                    }
                    else
                    {
                        if (existingScore == null)
                        {
                            var newScore = new LeaderboardScore
                            {
                                ClientId = targetClientId,
                                UserId = delivery.TargetUserId,
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

                            leaderboardChanged = true;
                        }
                        else if (incomingRevision > existingScore.Revision)
                        {
                            int affectedRows = await _context.LeaderboardScores
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

                            leaderboardChanged = affectedRows > 0;
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

                    // Publish changes after the transaction commits.
                    if (leaderboardChanged)
                    {
                        try
                        {
                            await _publisher.PublishAsync(
                                delivery.Period,
                                delivery.PeriodKey,
                                delivery.CountryCode,
                                CancellationToken.None);

                            _logger.LogInformation(
                                "Leaderboard update notification sent. " +
                                "OutboxId: {OutboxId}, Period: {Period}, " +
                                "PeriodKey: {PeriodKey}, CountryCode: {CountryCode}",
                                delivery.OutboxId,
                                delivery.Period,
                                delivery.PeriodKey,
                                delivery.CountryCode ?? "GLOBAL");
                        }
                        catch (Exception notificationException)
                        {
                            _logger.LogWarning(
                                notificationException,
                                "Leaderboard notification could not be sent. " +
                                "OutboxId: {OutboxId}",
                                delivery.OutboxId);
                        }
                    }

                    _logger.LogInformation(
                        "Leaderboard Delivery completed. " +
                        "DeliveryId: {DeliveryId}, Period: {Period}, " +
                        "IsRemoval: {IsRemoval}, Revision: {Revision}",
                        delivery.Id,
                        delivery.Period,
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

            return deliveryIds.Count == 100;
        }

        public async Task CleanupCompletedAsync(CancellationToken cancellationToken)
        {
            var cutoff = DateTime.UtcNow.AddDays(-30);
            await _context.LeaderboardCacheOutboxDeliveries
                .Where(x => x.Status == LeaderboardCacheOutboxDeliveryStatus.Completed &&
                            x.ProcessedAtUtc != null && x.ProcessedAtUtc < cutoff)
                .ExecuteDeleteAsync(cancellationToken);

            await _context.LeaderboardCacheOutboxes
                .Where(x => x.CreatedAtUtc < cutoff &&
                            !_context.LeaderboardCacheOutboxDeliveries.Any(d =>
                                d.OutboxId == x.Id &&
                                d.Status != LeaderboardCacheOutboxDeliveryStatus.Completed))
                .ExecuteDeleteAsync(cancellationToken);
        }

        // Kimliğin user: ile başlaması yeterli değildir.
        // Devamında pozitif ve geçerli bir kullanıcı ID'si bulunmalıdır.
        private static bool IsRegisteredIdentity(string targetClientId)
        {
            if (!targetClientId.StartsWith(
                "user:",
                StringComparison.Ordinal))
            {
                return false;
            }

            string userIdText = targetClientId["user:".Length..];

            return int.TryParse(userIdText, out int userId) &&
                   userId > 0;
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

    }
}
