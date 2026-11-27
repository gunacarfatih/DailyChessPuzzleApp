namespace DailyChessPuzzleApi.Models;

/// <summary>One immutable, idempotent profile XP award.</summary>
public sealed class ProfileProgress
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public int PuzzleAttemptId { get; set; }
    public PuzzleAttempt PuzzleAttempt { get; set; } = null!;
    public string Kind { get; set; } = string.Empty;
    public int Xp { get; set; }
    public int DifficultyBonus { get; set; }
    public int WrongMoves { get; set; }
    public bool UsedHint { get; set; }
    public DateOnly PlayedOn { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
