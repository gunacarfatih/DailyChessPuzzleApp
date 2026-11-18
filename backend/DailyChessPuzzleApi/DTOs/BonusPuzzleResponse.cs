
namespace DailyChessPuzzleApi.DTOs
{
    public class BonusPuzzleResponse
    {
        public bool IsEligible { get; set; }

        public bool IsCompleted { get; set; }

        public bool IsExpired { get; set; }

        public int? PuzzleId { get; set; }

        public string? Fen { get; set; }

        public List<string> PlayedMoves { get; set; } = new();

        public int CurrentMoveIndex { get; set; }

        public int WrongMoves { get; set; }

        public bool UsedHint { get; set; }

        public DateTime? StartedAtUtc { get; set; }

        public DateTime? CompletedAtUtc { get; set; }

        public int? RemainingSeconds { get; set; }

        public int? Score { get; set; }

        public string? HintSquare { get; set; }
    }
}
