
namespace DailyChessPuzzleApi.Models
{
    public sealed class ScoringResult
    {
        public decimal Score { get; init; }

        public int EffectiveRating { get; init; }

        public decimal RatingFactor { get; init; }

        public decimal MoveFactor { get; init; }

        public decimal ReferenceTimeSeconds { get; init; }

        public long TotalElapsedMilliseconds { get; init; }

        public long AnimationCompensationMilliseconds { get; init; }

        public long ServerCompensationMilliseconds { get; init; }

        public long AdjustedElapsedMilliseconds { get; init; }

        public decimal TimePenalty { get; init; }

        public decimal WrongMovePenalty { get; init; }

        public decimal HintPenalty { get; init; }

        public bool IsSuccessful { get; init; }

        public bool IsExpired { get; init; }
    }
}
