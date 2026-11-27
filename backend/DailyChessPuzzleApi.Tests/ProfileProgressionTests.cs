using DailyChessPuzzleApi.Services;
using Xunit;

namespace DailyChessPuzzleApi.Tests;

public sealed class ProfileProgressionTests
{
    [Theory]
    [InlineData(0, false, false, 150)]
    [InlineData(25, true, false, 150)]
    [InlineData(50, false, true, 175)]
    [InlineData(50, true, true, 150)]
    public void AwardXp_applies_base_difficulty_and_clean_play_bonus(
        int difficultyBonus, bool hasWrongMoves, bool usedHint, int expected)
    {
        Assert.Equal(expected, ProfileProgression.AwardXp(
            difficultyBonus, hasWrongMoves ? 1 : 0, usedHint));
    }

    [Theory]
    [InlineData(1199, 0)]
    [InlineData(1400, 25)]
    [InlineData(1999, 25)]
    [InlineData(2000, 50)]
    public void DifficultyBonus_uses_rating_band(int rating, int expected) =>
        Assert.Equal(expected, ProfileProgression.DifficultyBonus(rating));

    [Theory]
    [InlineData(0, 1)]
    [InlineData(99, 1)]
    [InlineData(100, 2)]
    [InlineData(400, 3)]
    public void LevelFor_uses_progression_thresholds(int xp, int expected) =>
        Assert.Equal(expected, ProfileProgression.LevelFor(xp));
}
