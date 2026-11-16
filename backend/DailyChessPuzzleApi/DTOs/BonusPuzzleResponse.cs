
namespace DailyChessPuzzleApi.DTOs
{
    public class BonusPuzzleResponse
    {
        public bool IsEligible { get; set; }
        public bool IsCompleted { get; set; }
        public bool IsExpired { get; set; }
        public int? PuzzleId { get; set; }
        public string? Fen { get; set; }
        public string? Moves { get; set; }
        public DateTime? StartedAtUtc { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
        public int? RemainingSeconds { get; set; }
        public int? Score { get; set; }
    }
}
