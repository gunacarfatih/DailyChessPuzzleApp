
namespace DailyChessPuzzleApi.Services.Interfaces
{
    public interface ILeaderboardOutboxService
    {
        // PostgreSQL'deki bekleyen görevleri Redis'e gönderir.
        Task<bool> ProcessPendingAsync(CancellationToken cancellationToken);

        Task CleanupCompletedAsync(CancellationToken cancellationToken);
    }
}
