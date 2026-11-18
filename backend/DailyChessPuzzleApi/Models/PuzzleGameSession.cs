
namespace DailyChessPuzzleApi.Models
{
    public class PuzzleGameSession
    {
        public int Id { get; set; }

        public int PuzzleId { get; set; }
        public Puzzle Puzzle { get; set; } = null!;

        // Kayıtlı kullanıcı veya misafir kimliği.
        public int? UserId { get; set; }
        public User? User { get; set; }

        public string? ClientId { get; set; }

        public DateOnly PuzzleDate { get; set; }

        // Lichess hamle dizisinde sıradaki beklenen hamlenin indeksi.
        public int CurrentMoveIndex { get; set; } = 1;

        public int WrongMoves { get; set; }

        public bool UsedHint { get; set; }

        public DateTime StartedAtUtc { get; set; }

        public DateTime? CompletedAtUtc { get; set; }

        public bool IsCompleted { get; set; }

        public DateTime CreatedAtUtc { get; set; }
    }
}
