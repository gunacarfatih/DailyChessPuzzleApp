
using Microsoft.Extensions.Options;

namespace DailyChessPuzzleApi.Settings
{
    public sealed class ScoringSettingsValidator
        : IValidateOptions<ScoringSettings>
    {
        public ValidateOptionsResult Validate(
            string? name,
            ScoringSettings options)
        {
            var errors = new List<string>();

            // 1. Puan sınırları.
            if (options.MaximumScore <= 0)
            {
                errors.Add(
                    "MaximumScore must be greater than zero.");
            }

            if (options.MinimumSuccessfulScore <= 0 ||
                options.MinimumSuccessfulScore > options.MaximumScore)
            {
                errors.Add(
                    "MinimumSuccessfulScore must be positive " +
                    "and cannot exceed MaximumScore.");
            }

            // 2. Ceza değerleri.
            if (options.MaximumTimePenalty < 0)
            {
                errors.Add(
                    "MaximumTimePenalty cannot be negative.");
            }

            if (options.MaximumWrongMovePenalty < 0)
            {
                errors.Add(
                    "MaximumWrongMovePenalty cannot be negative.");
            }

            if (options.HintPenalty < 0)
            {
                errors.Add(
                    "HintPenalty cannot be negative.");
            }

            if (options.MaximumTimePenalty +
                options.MaximumWrongMovePenalty +
                options.HintPenalty >
                options.MaximumScore - options.MinimumSuccessfulScore)
            {
                errors.Add(
                    "Total maximum penalties cannot exceed " +
                    "the difference between maximum and minimum score.");
            }

            // 3. Referans süre.
            if (options.BaseReferenceTimeSeconds <= 0)
            {
                errors.Add(
                    "BaseReferenceTimeSeconds must be positive.");
            }

            // 4. Rating sınırları.
            if (options.MinimumRating < 0)
            {
                errors.Add(
                    "MinimumRating cannot be negative.");
            }

            if (options.MaximumRating <= options.MinimumRating)
            {
                errors.Add(
                    "MaximumRating must exceed MinimumRating.");
            }

            if (options.ReferenceRating < options.MinimumRating ||
                options.ReferenceRating > options.MaximumRating)
            {
                errors.Add(
                    "ReferenceRating must be within the rating range.");
            }

            if (options.RatingScale <= 0)
            {
                errors.Add(
                    "RatingScale must be greater than zero.");
            }
            else if (options.RatingScale <=
                     (decimal)options.ReferenceRating -
                     options.MinimumRating)
            {
                errors.Add(
                    "RatingScale must produce a positive " +
                    "rating factor at the minimum rating.");
            }

            // 5. Ceza katsayıları.
            if (options.TimePenaltyCoefficient <= 0)
            {
                errors.Add(
                    "TimePenaltyCoefficient must be positive.");
            }

            if (options.WrongMovePenaltyCoefficient <= 0)
            {
                errors.Add(
                    "WrongMovePenaltyCoefficient must be positive.");
            }

            // 6. Animasyon telafisi.
            if (options.OpponentAnimationCompensationMilliseconds < 0)
            {
                errors.Add(
                    "Animation compensation cannot be negative.");
            }

            // 7. Sunucu işlem süresi telafisi.
            if (options.MaximumServerCompensationPerRequestMilliseconds < 0)
            {
                errors.Add(
                    "Server compensation per request cannot be negative.");
            }

            if (options.MaximumServerCompensationPerSessionMilliseconds < 0)
            {
                errors.Add(
                    "Server compensation per session cannot be negative.");
            }

            if (options.MaximumServerCompensationPerRequestMilliseconds >
                options.MaximumServerCompensationPerSessionMilliseconds)
            {
                errors.Add(
                    "Per-request compensation cannot exceed " +
                    "per-session compensation.");
            }

            // 8. Bonus süre sınırı.
            if (options.BonusTimeLimitSeconds <= 0)
            {
                errors.Add(
                    "BonusTimeLimitSeconds must be positive.");
            }

            // 9. Puan hassasiyeti.
            if (options.ScoreDecimalPlaces != 4)
            {
                errors.Add(
                    "ScoreDecimalPlaces must be exactly 4.");
            }

            return errors.Count == 0
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail(errors);
        }
    }
}
