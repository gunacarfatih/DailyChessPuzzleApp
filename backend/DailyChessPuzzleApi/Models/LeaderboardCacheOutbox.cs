
namespace DailyChessPuzzleApi.Models
{
    public class LeaderboardCacheOutbox
    {
        public int Id { get; set; }

        // Normal bulmaca işlemlerinde ilgili deneme kaydı.
        // Hesap birleştirme gibi toplu işlemlerde null olabilir.
        public int? PuzzleAttemptId { get; set; }

        public string ClientId { get; set; } = string.Empty;

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public ICollection<LeaderboardCacheOutboxDelivery> Deliveries { get; set; }
            = new List<LeaderboardCacheOutboxDelivery>();
    }
}
