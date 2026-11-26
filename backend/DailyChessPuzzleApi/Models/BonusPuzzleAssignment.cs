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

        // Bonus bulmaca ilk açıldığında sunucu tarafından atanır.
        public DateTime? StartedAtUtc { get; set; }

        // Bonus tamamlandığında sunucu tarafından atanır.
        public DateTime? CompletedAtUtc { get; set; }

        // Bonus sonucu kaydedildi mi?
        public bool IsCompleted { get; set; }

        // Bonus için oluşturulan PuzzleAttempt kaydı.
        public int? PuzzleAttemptId { get; set; }

        public PuzzleAttempt? PuzzleAttempt { get; set; }

        public DateTime CreatedAtUtc { get; set; }
    }
}