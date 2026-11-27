
namespace DailyChessPuzzleApi.Models
{
    public sealed class ScoringInput
    {
        public int Rating { get; init; }

        public int RequiredPlayerMoves { get; init; }

        public int WrongMoves { get; init; }

        public bool UsedHint { get; init; }

        public long TotalElapsedMilliseconds { get; init; }

        public int CompensatedOpponentMoves { get; init; }

        public long ServerCompensationMilliseconds { get; init; }

        public bool IsCompleted { get; init; }

        public bool IsBonus { get; init; }
    }
}
