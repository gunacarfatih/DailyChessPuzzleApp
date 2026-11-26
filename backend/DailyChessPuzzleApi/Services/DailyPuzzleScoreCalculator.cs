namespace DailyChessPuzzleApi.Services;

public static class DailyPuzzleScoreCalculator
{
    public static int Calculate(int seconds, int wrongMoves, bool usedHint)
    {
        var safeSeconds = Math.Max(0, seconds);
        var safeWrongMoves = Math.Max(0, wrongMoves);
        var score = 1000L -
                    ((long)safeSeconds * 2) -
                    ((long)safeWrongMoves * 150) -
                    (usedHint ? 250L : 0L);

        return (int)Math.Max(0L, score);
    }
}
