using DailyChessPuzzleApi.Hubs;
using DailyChessPuzzleApi.Services.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace DailyChessPuzzleApi.Services
{
    public sealed class LeaderboardPublisher
        : ILeaderboardPublisher
    {
        private readonly IHubContext<LeaderboardHub> _hubContext;

        public LeaderboardPublisher(
            IHubContext<LeaderboardHub> hubContext)
        {
            _hubContext = hubContext;
        }

        public async Task PublishAsync(
            string period,
            string periodKey,
            string? countryCode,
            CancellationToken cancellationToken = default)
        {
            var normalizedPeriod =
                period.Trim().ToLowerInvariant();

            if (normalizedPeriod is not
                ("daily" or "weekly" or "monthly" or "alltime"))
            {
                throw new ArgumentException(
                    "Invalid leaderboard period.",
                    nameof(period));
            }

            var normalizedCountry =
                string.IsNullOrWhiteSpace(countryCode)
                    ? null
                    : countryCode.Trim().ToUpperInvariant();

            var groupName = normalizedCountry == null
                ? $"leaderboard:{normalizedPeriod}"
                : $"leaderboard:{normalizedPeriod}:{normalizedCountry}";

            var eventPayload = new
            {
                period = normalizedPeriod,
                periodKey,
                countryCode = normalizedCountry,
                globalChanged = normalizedCountry == null
            };

            await _hubContext.Clients
                .Group(groupName)
                .SendAsync(
                    "LeaderboardUpdated",
                    eventPayload,
                    cancellationToken);
        }
    }
}