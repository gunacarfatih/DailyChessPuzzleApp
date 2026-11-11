namespace DailyChessPuzzleApi.DTOs
{
    public class PuzzleTodayResponse
    {
        public int Id { get; set; }

        public string Fen { get; set; } = string.Empty;

        public string Moves { get; set; } = string.Empty;

        public string Difficulty { get; set; } = string.Empty;

        // Kullanıcının bu bulmacaya daha önce sonuç gönderip göndermediği.
        public bool IsSolved { get; set; }

        // Daha önce sonuç gönderildiyse kazanılan puan.
        public int? PreviousScore { get; set; }
    }
}