
using DailyChessPuzzleApi.Models;
using DailyChessPuzzleApi.Services.Interfaces;
using DailyChessPuzzleApi.Settings;
using Microsoft.Extensions.Options;

namespace DailyChessPuzzleApi.Services
{
    public sealed class ScoringService : IScoringService
    {
        private readonly ScoringSettings _settings;

        public ScoringService(IOptions<ScoringSettings> options)
        {
            _settings = options.Value;
        }

        public ScoringResult Calculate(ScoringInput input)
        {
            ArgumentNullException.ThrowIfNull(input);

            ValidateInput(input);

            // 1. Rating normalizasyonu.
            int effectiveRating = Math.Clamp(
                input.Rating,
                _settings.MinimumRating,
                _settings.MaximumRating);

            decimal ratingFactor =
                1m +
                ((decimal)effectiveRating - _settings.ReferenceRating)
                / _settings.RatingScale;

            // 2. Hamle katsayısı.
            decimal moveFactor =
                (decimal)Math.Sqrt(input.RequiredPlayerMoves);

            // 3. Referans çözüm süresi.
            decimal referenceTimeSeconds =
                _settings.BaseReferenceTimeSeconds
                * ratingFactor
                * moveFactor;

            // 4. Animasyon telafisi.
            long requestedAnimationCompensation =
                (long)input.CompensatedOpponentMoves
                * _settings.OpponentAnimationCompensationMilliseconds;

            long animationCompensationMilliseconds = Math.Min(
                requestedAnimationCompensation,
                input.TotalElapsedMilliseconds);

            // 5. Sunucu işlem süresi telafisi.
            long remainingElapsedMilliseconds =
                input.TotalElapsedMilliseconds
                - animationCompensationMilliseconds;

            long serverCompensationMilliseconds = Math.Min(
                input.ServerCompensationMilliseconds,
                _settings.MaximumServerCompensationPerSessionMilliseconds);

            serverCompensationMilliseconds = Math.Min(
                serverCompensationMilliseconds,
                remainingElapsedMilliseconds);

            // 6. Düzeltilmiş çözüm süresi.
            long adjustedElapsedMilliseconds =
                remainingElapsedMilliseconds
                - serverCompensationMilliseconds;

            decimal adjustedElapsedSeconds =
                adjustedElapsedMilliseconds / 1000m;

            // 7. Doğrusal süre cezası.
            decimal timePenalty = Math.Min(
                _settings.MaximumTimePenalty,
                _settings.TimePenaltyCoefficient
                * adjustedElapsedSeconds
                / referenceTimeSeconds);

            // 8. Karesel yanlış hamle cezası.
            decimal wrongMoves = input.WrongMoves;

            decimal wrongMovePenalty = Math.Min(
                _settings.MaximumWrongMovePenalty,
                _settings.WrongMovePenaltyCoefficient
                * wrongMoves
                * wrongMoves
                / moveFactor);

            // 9. İpucu cezası.
            decimal hintPenalty = input.UsedHint
                ? _settings.HintPenalty
                : 0m;

            // 10. Bonus süre sınırı.
            bool isExpired =
                input.IsBonus &&
                input.TotalElapsedMilliseconds >=
                (long)_settings.BonusTimeLimitSeconds * 1000L;

            // 11. Başarı kontrolü.
            bool isSuccessful =
                input.IsCompleted && !isExpired;

            // 12. Nihai puan.
            decimal score = 0m;

            if (isSuccessful)
            {
                decimal calculatedScore =
                    _settings.MaximumScore
                    - timePenalty
                    - wrongMovePenalty
                    - hintPenalty;

                calculatedScore = Math.Clamp(
                    calculatedScore,
                    _settings.MinimumSuccessfulScore,
                    _settings.MaximumScore);

                // Yalnızca nihai puan yuvarlanır.
                score = Math.Round(
                    calculatedScore,
                    _settings.ScoreDecimalPlaces,
                    MidpointRounding.AwayFromZero);
            }

            // 13. Hesaplama sonucunun oluşturulması.
            return new ScoringResult
            {
                Score = score,

                EffectiveRating = effectiveRating,

                RatingFactor = ratingFactor,

                MoveFactor = moveFactor,

                ReferenceTimeSeconds = referenceTimeSeconds,

                TotalElapsedMilliseconds =
                    input.TotalElapsedMilliseconds,

                AnimationCompensationMilliseconds =
                    animationCompensationMilliseconds,

                ServerCompensationMilliseconds =
                    serverCompensationMilliseconds,

                AdjustedElapsedMilliseconds =
                    adjustedElapsedMilliseconds,

                TimePenalty = timePenalty,

                WrongMovePenalty = wrongMovePenalty,

                HintPenalty = hintPenalty,

                IsSuccessful = isSuccessful,

                IsExpired = isExpired
            };
        }

        private static void ValidateInput(ScoringInput input)
        {
            if (input.RequiredPlayerMoves <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(input.RequiredPlayerMoves),
                    "Required player moves must be greater than zero.");
            }

            if (input.WrongMoves < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(input.WrongMoves));
            }

            if (input.TotalElapsedMilliseconds < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(input.TotalElapsedMilliseconds));
            }

            if (input.CompensatedOpponentMoves < 0 ||
                input.CompensatedOpponentMoves >
                input.RequiredPlayerMoves - 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(input.CompensatedOpponentMoves));
            }

            if (input.ServerCompensationMilliseconds < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(input.ServerCompensationMilliseconds));
            }
        }
    }
}
