namespace DailyChessPuzzleApi.Models
{
    public class DailyPuzzle
    {
        public int Id { get; set; }

        public int PuzzleId { get; set; }

        public Puzzle Puzzle { get; set; } = null!;

        public DateOnly PuzzleDate { get; set; }

        // 0 = Guest, 1 = Registered
        public PuzzleAudience Audience { get; set; }
    }

    public enum PuzzleAudience
    {
        Guest = 0,
        Registered = 1
    }
}