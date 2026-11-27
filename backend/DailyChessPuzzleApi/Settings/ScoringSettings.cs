
namespace DailyChessPuzzleApi.Settings
{
    public class ScoringSettings
    {
        public const string SectionName = "Scoring";

        // Temel puan sınırları.
        public decimal MaximumScore { get; set; } = 1000m;

        public decimal MinimumSuccessfulScore { get; set; } = 100m;

        // Ceza sınırları.
        public decimal MaximumTimePenalty { get; set; } = 300m;

        public decimal MaximumWrongMovePenalty { get; set; } = 400m;

        public decimal HintPenalty { get; set; } = 200m;

        // Referans süre hesaplaması.
        public decimal BaseReferenceTimeSeconds { get; set; } = 20m;

        // Rating normalizasyonu.
        public int MinimumRating { get; set; } = 800;

        public int MaximumRating { get; set; } = 3200;

        public int ReferenceRating { get; set; } = 800;

        public decimal RatingScale { get; set; } = 1600m;

        // Ceza katsayıları.
        public decimal TimePenaltyCoefficient { get; set; } = 150m;

        public decimal WrongMovePenaltyCoefficient { get; set; } = 50m;

        // Rakip hamlesi animasyon telafisi.
        public int OpponentAnimationCompensationMilliseconds
        {
            get;
            set;
        } = 500;

        // Sunucu işlem süresi telafisi.
        public int MaximumServerCompensationPerRequestMilliseconds
        {
            get;
            set;
        } = 250;

        public int MaximumServerCompensationPerSessionMilliseconds
        {
            get;
            set;
        } = 2000;

        // Bonus bulmaca süre sınırı.
        public int BonusTimeLimitSeconds { get; set; } = 300;

        // Nihai puan hassasiyeti.
        public int ScoreDecimalPlaces { get; set; } = 4;
    }
}
