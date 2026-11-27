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

        // Oturum başlangıcında sabitlenen ülke.
        public string? CountryCode { get; set; }

        public DateOnly PuzzleDate { get; set; }

        // Lichess hamle dizisinde sıradaki beklenen hamlenin indeksi.
        public int CurrentMoveIndex { get; set; } = 1;

        public int WrongMoves { get; set; }

        public bool UsedHint { get; set; }

        public DateTime StartedAtUtc { get; set; }

        public DateTime? CompletedAtUtc { get; set; }

        // Telafi uygulanabilecek rakip hamlesi sayısı.
        public int CompensatedOpponentMoves { get; set; }

        // Birikmiş sunucu işlem süresi telafisi.
        public long ServerCompensationMilliseconds { get; set; }

        // Zorunlu animasyonun bitiş zamanı.
        public DateTime? NextMoveAllowedAtUtc { get; set; }

        // Oturumun toplam geçen süresi.
        public long? TotalElapsedMilliseconds { get; set; }

        // Puanlama sırasında uygulanan animasyon telafisi.
        public long? AnimationCompensationMilliseconds { get; set; }

        // Puanlamada kullanılan net çözüm süresi.
        public long? AdjustedElapsedMilliseconds { get; set; }

        public decimal? CompletedScore { get; set; }

        public bool IsCompleted { get; set; }

        public DateTime CreatedAtUtc { get; set; }
    }
}