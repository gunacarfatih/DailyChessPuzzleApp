
using DailyChessPuzzleApi.Models;
using DailyChessPuzzleApi.Services;
using DailyChessPuzzleApi.Settings;
using Microsoft.Extensions.Options;
using Xunit;

namespace DailyChessPuzzleApi.Tests
{
    public sealed class ScoringServiceTests
    {
        private readonly ScoringService _service;

        public ScoringServiceTests()
        {
            _service = new ScoringService(
                Options.Create(new ScoringSettings()));
        }

        private static ScoringInput CreateInput(
            int rating = 800,
            int requiredMoves = 1,
            int wrongMoves = 0,
            bool usedHint = false,
            long elapsedMilliseconds = 0,
            int compensatedOpponentMoves = 0,
            long serverCompensationMilliseconds = 0,
            bool isCompleted = true,
            bool isBonus = false)
        {
            return new ScoringInput
            {
                Rating = rating,
                RequiredPlayerMoves = requiredMoves,
                WrongMoves = wrongMoves,
                UsedHint = usedHint,
                TotalElapsedMilliseconds = elapsedMilliseconds,
                CompensatedOpponentMoves = compensatedOpponentMoves,
                ServerCompensationMilliseconds =
                    serverCompensationMilliseconds,
                IsCompleted = isCompleted,
                IsBonus = isBonus
            };
        }

        [Fact]
        public void Calculate_PerfectCompletion_ReturnsMaximumScore()
        {
            var result = _service.Calculate(CreateInput());

            Assert.Equal(1000m, result.Score);
            Assert.True(result.IsSuccessful);
            Assert.False(result.IsExpired);
        }

        [Fact]
        public void Calculate_MaximumPenalties_ReturnsMinimumScore()
        {
            var result = _service.Calculate(CreateInput(
                wrongMoves: 100,
                usedHint: true,
                elapsedMilliseconds: 600000));

            Assert.Equal(300m, result.TimePenalty);
            Assert.Equal(400m, result.WrongMovePenalty);
            Assert.Equal(200m, result.HintPenalty);
            Assert.Equal(100m, result.Score);
        }

        [Fact]
        public void Calculate_IncompletePuzzle_ReturnsZero()
        {
            var result = _service.Calculate(CreateInput(
                isCompleted: false));

            Assert.Equal(0m, result.Score);
            Assert.False(result.IsSuccessful);
        }

        [Theory]
        [InlineData(400, 800)]
        [InlineData(800, 800)]
        [InlineData(1800, 1800)]
        [InlineData(3200, 3200)]
        [InlineData(4000, 3200)]
        public void Calculate_Rating_IsClamped(
            int rating,
            int expectedRating)
        {
            var result = _service.Calculate(CreateInput(
                rating: rating));

            Assert.Equal(expectedRating, result.EffectiveRating);
        }

        [Theory]
        [InlineData(800, 1.0)]
        [InlineData(1600, 1.5)]
        [InlineData(2400, 2.0)]
        [InlineData(3200, 2.5)]
        public void Calculate_RatingFactor_IsCorrect(
            int rating,
            double expectedFactor)
        {
            var result = _service.Calculate(CreateInput(
                rating: rating));

            Assert.Equal(
                (decimal)expectedFactor,
                result.RatingFactor);
        }

        [Theory]
        [InlineData(1, 1)]
        [InlineData(4, 2)]
        [InlineData(9, 3)]
        [InlineData(16, 4)]
        public void Calculate_MoveFactor_UsesSquareRoot(
            int requiredMoves,
            int expectedFactor)
        {
            var result = _service.Calculate(CreateInput(
                requiredMoves: requiredMoves));

            Assert.Equal(
                (decimal)expectedFactor,
                result.MoveFactor);
        }

        [Fact]
        public void Calculate_ReferenceTime_IsCorrect()
        {
            var result = _service.Calculate(CreateInput(
                rating: 1800,
                requiredMoves: 4));

            Assert.Equal(65m, result.ReferenceTimeSeconds);
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(10000, 75)]
        [InlineData(20000, 150)]
        [InlineData(30000, 225)]
        [InlineData(40000, 300)]
        [InlineData(60000, 300)]
        public void Calculate_TimePenalty_IsLinearAndCapped(
            long elapsedMilliseconds,
            int expectedPenalty)
        {
            var result = _service.Calculate(CreateInput(
                elapsedMilliseconds: elapsedMilliseconds));

            Assert.Equal(
                (decimal)expectedPenalty,
                result.TimePenalty);
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 25)]
        [InlineData(2, 100)]
        [InlineData(3, 225)]
        [InlineData(4, 400)]
        [InlineData(5, 400)]
        [InlineData(10, 400)]
        public void Calculate_WrongMovePenalty_IsQuadraticAndCapped(
            int wrongMoves,
            int expectedPenalty)
        {
            var result = _service.Calculate(CreateInput(
                requiredMoves: 4,
                wrongMoves: wrongMoves));

            Assert.Equal(
                (decimal)expectedPenalty,
                result.WrongMovePenalty);
        }

        [Theory]
        [InlineData(false, 0)]
        [InlineData(true, 200)]
        public void Calculate_HintPenalty_IsCorrect(
            bool usedHint,
            int expectedPenalty)
        {
            var result = _service.Calculate(CreateInput(
                usedHint: usedHint));

            Assert.Equal(
                (decimal)expectedPenalty,
                result.HintPenalty);
        }

        [Fact]
        public void Calculate_AnimationCompensation_IsCorrect()
        {
            var result = _service.Calculate(CreateInput(
                requiredMoves: 4,
                elapsedMilliseconds: 10000,
                compensatedOpponentMoves: 3));

            Assert.Equal(
                1500L,
                result.AnimationCompensationMilliseconds);

            Assert.Equal(
                8500L,
                result.AdjustedElapsedMilliseconds);
        }

        [Fact]
        public void Calculate_ServerCompensation_IsCapped()
        {
            var result = _service.Calculate(CreateInput(
                elapsedMilliseconds: 10000,
                serverCompensationMilliseconds: 5000));

            Assert.Equal(
                2000L,
                result.ServerCompensationMilliseconds);

            Assert.Equal(
                8000L,
                result.AdjustedElapsedMilliseconds);
        }

        [Fact]
        public void Calculate_CombinedCompensation_IsCorrect()
        {
            var result = _service.Calculate(CreateInput(
                requiredMoves: 4,
                elapsedMilliseconds: 30000,
                compensatedOpponentMoves: 3,
                serverCompensationMilliseconds: 350));

            Assert.Equal(
                1500L,
                result.AnimationCompensationMilliseconds);

            Assert.Equal(
                350L,
                result.ServerCompensationMilliseconds);

            Assert.Equal(
                28150L,
                result.AdjustedElapsedMilliseconds);
        }

        [Fact]
        public void Calculate_AdjustedTime_CannotBeNegative()
        {
            var result = _service.Calculate(CreateInput(
                requiredMoves: 4,
                elapsedMilliseconds: 1000,
                compensatedOpponentMoves: 3,
                serverCompensationMilliseconds: 2000));

            Assert.Equal(
                0L,
                result.AdjustedElapsedMilliseconds);

            Assert.Equal(0m, result.TimePenalty);
        }

        [Fact]
        public void Calculate_FinalScore_IsRoundedToFourDecimals()
        {
            var result = _service.Calculate(CreateInput(
                rating: 1800,
                requiredMoves: 4,
                wrongMoves: 2,
                elapsedMilliseconds: 30000,
                compensatedOpponentMoves: 3,
                serverCompensationMilliseconds: 350));

            Assert.Equal(835.0385m, result.Score);
        }

        [Fact]
        public void Calculate_BonusAndMain_UseSameScoringFormula()
        {
            var main = _service.Calculate(CreateInput(
                rating: 1800,
                requiredMoves: 4,
                elapsedMilliseconds: 30000));

            var bonus = _service.Calculate(CreateInput(
                rating: 1800,
                requiredMoves: 4,
                elapsedMilliseconds: 30000,
                isBonus: true));

            Assert.Equal(main.Score, bonus.Score);
        }

        [Theory]
        [InlineData(299999, false)]
        [InlineData(300000, true)]
        [InlineData(300001, true)]
        public void Calculate_BonusExpiration_UsesRawElapsedTime(
            long elapsedMilliseconds,
            bool expectedExpired)
        {
            var result = _service.Calculate(CreateInput(
                elapsedMilliseconds: elapsedMilliseconds,
                serverCompensationMilliseconds: 2000,
                isBonus: true));

            Assert.Equal(expectedExpired, result.IsExpired);
            Assert.Equal(!expectedExpired, result.IsSuccessful);

            if (expectedExpired)
            {
                Assert.Equal(0m, result.Score);
            }
            else
            {
                Assert.True(result.Score >= 100m);
            }
        }

        [Fact]
        public void Calculate_MainPuzzle_DoesNotUseBonusTimeLimit()
        {
            var result = _service.Calculate(CreateInput(
                elapsedMilliseconds: 600000));

            Assert.False(result.IsExpired);
            Assert.True(result.IsSuccessful);
            Assert.Equal(700m, result.Score);
        }

        [Fact]
        public void Calculate_NullInput_ThrowsException()
        {
            Assert.Throws<ArgumentNullException>(
                () => _service.Calculate(null!));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void Calculate_InvalidRequiredMoves_ThrowsException(
            int requiredMoves)
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => _service.Calculate(CreateInput(
                    requiredMoves: requiredMoves)));
        }

        [Fact]
        public void Calculate_NegativeWrongMoves_ThrowsException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => _service.Calculate(CreateInput(
                    wrongMoves: -1)));
        }

        [Fact]
        public void Calculate_NegativeElapsedTime_ThrowsException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => _service.Calculate(CreateInput(
                    elapsedMilliseconds: -1)));
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(4)]
        public void Calculate_InvalidAnimationCount_ThrowsException(
            int compensatedOpponentMoves)
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => _service.Calculate(CreateInput(
                    requiredMoves: 4,
                    compensatedOpponentMoves:
                        compensatedOpponentMoves)));
        }

        [Fact]
        public void Calculate_NegativeServerCompensation_ThrowsException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => _service.Calculate(CreateInput(
                    serverCompensationMilliseconds: -1)));
        }
    }
}
