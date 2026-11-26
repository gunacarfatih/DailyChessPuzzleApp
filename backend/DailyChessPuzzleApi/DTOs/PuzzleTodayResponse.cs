
namespace DailyChessPuzzleApi.DTOs
{
    public class PuzzleTodayResponse
    {
        public int Id { get; set; }

        public string Difficulty { get; set; } = string.Empty;

        public bool IsSolved { get; set; }

        public int? PreviousScore { get; set; }
    }
}
