
using Microsoft.AspNetCore.SignalR;

namespace DailyChessPuzzleApi.Hubs;

public class LeaderboardHub : Hub
{
    public Task JoinLeaderboard(string period, string? countryCode = null)
    {
        var normalizedPeriod = period.Trim().ToLowerInvariant();
        if (normalizedPeriod is not ("daily" or "weekly" or "monthly" or "alltime"))
            throw new HubException("INVALID_LEADERBOARD_PERIOD");

        var tasks = new List<Task>
        {
            Groups.AddToGroupAsync(Context.ConnectionId, $"leaderboard:{normalizedPeriod}")
        };
        var normalizedCountry = countryCode?.Trim().ToUpperInvariant();
        if (normalizedCountry is { Length: 2 } && normalizedCountry.All(char.IsLetter))
            tasks.Add(Groups.AddToGroupAsync(Context.ConnectionId,
                $"leaderboard:{normalizedPeriod}:{normalizedCountry}"));
        return Task.WhenAll(tasks);
    }

    public Task LeaveLeaderboard(string period, string? countryCode = null)
    {
        var normalizedPeriod = period.Trim().ToLowerInvariant();
        var tasks = new List<Task>
        {
            Groups.RemoveFromGroupAsync(Context.ConnectionId, $"leaderboard:{normalizedPeriod}")
        };
        var normalizedCountry = countryCode?.Trim().ToUpperInvariant();
        if (normalizedCountry is { Length: 2 } && normalizedCountry.All(char.IsLetter))
            tasks.Add(Groups.RemoveFromGroupAsync(Context.ConnectionId,
                $"leaderboard:{normalizedPeriod}:{normalizedCountry}"));
        return Task.WhenAll(tasks);
    }
}
