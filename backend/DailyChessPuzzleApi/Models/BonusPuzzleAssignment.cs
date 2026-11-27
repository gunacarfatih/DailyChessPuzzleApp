namespace DailyChessPuzzleApi.Models
{
    public class BonusPuzzleAssignment
    {
        public int Id { get; set; }

        public int UserId { get; set; }

        public User User { get; set; } = null!;

        public int PuzzleId { get; set; }

        public Puzzle Puzzle { get; set; } = null!;

        public DateOnly PuzzleDate { get; set; }

        // Bonus ilk başlatıldığında sabitlenen ülke.
        public string? CountryCode { get; set; }

        public int CurrentMoveIndex { get; set; } = 1;

        public int WrongMoves { get; set; }

        public bool UsedHint { get; set; }

        // Bonus bulmacanın sunucu tarafındaki başlangıç zamanı.
        public DateTime? StartedAtUtc { get; set; }

        // Son doğru hamlenin sunucu tarafındaki zamanı.
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

        // Bonus sonucu kaydedildi mi?
        public bool IsCompleted { get; set; }

        // Bonus için oluşturulan PuzzleAttempt kaydı.
        public int? PuzzleAttemptId { get; set; }

        public PuzzleAttempt? PuzzleAttempt { get; set; }

        public DateTime CreatedAtUtc { get; set; }
    }
}