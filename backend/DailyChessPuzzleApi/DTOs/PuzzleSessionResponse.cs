namespace DailyChessPuzzleApi.DTOs
{
    public class PuzzleSessionResponse
    {
        public int PuzzleId { get; set; }
        public int CurrentMoveIndex { get; set; }
        public int WrongMoves { get; set; }
        public bool UsedHint { get; set; }
        public bool IsCompleted { get; set; }
        public DateTime StartedAtUtc { get; set; }
    }
}