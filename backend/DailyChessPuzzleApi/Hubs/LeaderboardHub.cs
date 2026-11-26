using Microsoft.AspNetCore.SignalR;

namespace DailyChessPuzzleApi.Hubs;

public class LeaderboardHub : Hub
{
    private static readonly string[] SupportedPeriods =
    {
        "daily",
        "weekly",
        "monthly",
        "alltime"
    };

    public Task JoinLeaderboard(
        string period,
        string? countryCode = null)
    {
        var normalizedPeriod = NormalizePeriod(period);
        var normalizedCountry = NormalizeCountry(countryCode);

        var tasks = new List<Task>
        {
            Groups.AddToGroupAsync(
                Context.ConnectionId,
                $"leaderboard:{normalizedPeriod}")
        };

        if (normalizedCountry != null)
        {
            tasks.Add(
                Groups.AddToGroupAsync(
                    Context.ConnectionId,
                    $"leaderboard:{normalizedPeriod}:{normalizedCountry}"));
        }

        return Task.WhenAll(tasks);
    }

    public Task LeaveLeaderboard(
        string period,
        string? countryCode = null)
    {
        var normalizedPeriod = NormalizePeriod(period);
        var normalizedCountry = NormalizeCountry(countryCode);

        var tasks = new List<Task>
        {
            Groups.RemoveFromGroupAsync(
                Context.ConnectionId,
                $"leaderboard:{normalizedPeriod}")
        };

        if (normalizedCountry != null)
        {
            tasks.Add(
                Groups.RemoveFromGroupAsync(
                    Context.ConnectionId,
                    $"leaderboard:{normalizedPeriod}:{normalizedCountry}"));
        }

        return Task.WhenAll(tasks);
    }

    public Task JoinAllLeaderboards(
        string? countryCode = null)
    {
        return Task.WhenAll(
            SupportedPeriods.Select(
                period => JoinLeaderboard(
                    period,
                    countryCode)));
    }

    public Task LeaveAllLeaderboards(
        string? countryCode = null)
    {
        return Task.WhenAll(
            SupportedPeriods.Select(
                period => LeaveLeaderboard(
                    period,
                    countryCode)));
    }

    private static string NormalizePeriod(string period)
    {
        var normalizedPeriod =
            period?.Trim().ToLowerInvariant();

        if (normalizedPeriod is not
            ("daily" or "weekly" or "monthly" or "alltime"))
        {
            throw new HubException(
                "INVALID_LEADERBOARD_PERIOD");
        }

        return normalizedPeriod;
    }

    private static string? NormalizeCountry(
        string? countryCode)
    {
        var normalizedCountry =
            countryCode?.Trim().ToUpperInvariant();

        if (normalizedCountry is { Length: 2 } &&
            normalizedCountry.All(
                character => character is >= 'A' and <= 'Z'))
        {
            return normalizedCountry;
        }

        return null;
    }
}