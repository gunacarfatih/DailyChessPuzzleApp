using DailyChessPuzzleApi.Services;
using Xunit;

namespace DailyChessPuzzleApi.Tests;

public class DailyPuzzleScoreCalculatorTests
{
    [Theory]
    [InlineData(0, 0, false, 1000)]
    [InlineData(10, 1, false, 830)]
    [InlineData(10, 1, true, 580)]
    [InlineData(600, 0, false, 0)]
    [InlineData(-10, -1, false, 1000)]
    public void Calculate_applies_server_scoring_rules(
        int seconds,
        int wrongMoves,
        bool usedHint,
        int expected)
    {
        Assert.Equal(
            expected,
            DailyPuzzleScoreCalculator.Calculate(seconds, wrongMoves, usedHint));
    }
}
