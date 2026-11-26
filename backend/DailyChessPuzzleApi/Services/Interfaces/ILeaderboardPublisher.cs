namespace DailyChessPuzzleApi.Services.Interfaces
{
    public interface ILeaderboardPublisher
    {
        Task PublishAsync(
            string period,
            string periodKey,
            string? countryCode,
            CancellationToken cancellationToken = default);
    }
}