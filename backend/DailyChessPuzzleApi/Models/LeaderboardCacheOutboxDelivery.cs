
namespace DailyChessPuzzleApi.Models
{
    public class LeaderboardCacheOutboxDelivery
    {
        public int Id { get; set; }

        public int OutboxId { get; set; }

        public LeaderboardCacheOutbox Outbox { get; set; } = null!;

        // İşlemin uygulanacağı leaderboard kimliği.
        public string TargetClientId { get; set; } = string.Empty;

        // true ise ilgili kimlik leaderboard'dan kaldırılır.
        public bool IsRemoval { get; set; }

        public string Period { get; set; } = string.Empty;

        public string PeriodKey { get; set; } = string.Empty;

        public string? CountryCode { get; set; }

        public string CacheKey { get; set; } = string.Empty;

        public int TotalScore { get; set; }

        public int SolvedCount { get; set; }

        public LeaderboardCacheOutboxDeliveryStatus Status { get; set; }
            = LeaderboardCacheOutboxDeliveryStatus.Pending;

        public int RetryCount { get; set; }

        public DateTime? NextRetryAtUtc { get; set; }

        public string? LastError { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime? ProcessedAtUtc { get; set; }

        public string? LockedBy { get; set; }

        public DateTime? LockedUntilUtc { get; set; }
    }
}
