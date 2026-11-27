
namespace DailyChessPuzzleApi.DTOs
{
    public class PuzzleSubmitResponse
    {
        public decimal Score { get; set; }

        public int Seconds { get; set; }

        public int WrongMoves { get; set; }

        public bool UsedHint { get; set; }

        public string Message { get; set; } = string.Empty;
    }
}
