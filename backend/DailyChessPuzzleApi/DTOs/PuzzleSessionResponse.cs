
namespace DailyChessPuzzleApi.DTOs
{
    public class PuzzleSessionResponse
    {
        // Günlük puzzle oturum kimliği.
        // Bonus puzzle yanıtlarında null olabilir.
        public int? SessionId { get; set; }

        public int PuzzleId { get; set; }

        public string? Fen { get; set; }

        public List<string> PlayedMoves { get; set; } = new();

        public int CurrentMoveIndex { get; set; }

        public int WrongMoves { get; set; }

        public bool UsedHint { get; set; }

        public bool IsSolved { get; set; }

        public bool IsCompleted { get; set; }

        public DateTime StartedAtUtc { get; set; }

        public string? HintSquare { get; set; }

        public int? RemainingSeconds { get; set; }

        public decimal? Score { get; set; }
    }
}
