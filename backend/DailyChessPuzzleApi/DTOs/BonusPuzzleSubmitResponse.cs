
namespace DailyChessPuzzleApi.DTOs
{
    public class BonusPuzzleSubmitResponse
    {
        public decimal Score { get; set; }

        public int Seconds { get; set; }

        public bool IsCompleted { get; set; }

        public string Message { get; set; } = string.Empty;
    }
}
