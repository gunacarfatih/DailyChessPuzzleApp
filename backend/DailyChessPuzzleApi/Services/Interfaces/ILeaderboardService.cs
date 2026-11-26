
using DailyChessPuzzleApi.DTOs;

namespace DailyChessPuzzleApi.Services.Interfaces
{
    public interface ILeaderboardService
    {
        Task<LeaderboardEntryDto?> GetUserStatsAsync(
            int userId,
            CancellationToken cancellationToken = default);

        Task<LeaderboardResponse> GetLeaderboardAsync(
            string? country,
            int? userId,
            int limit,
            DateOnly? startDate,
            DateOnly? endDate,
            string? period = null,
            CancellationToken cancellationToken = default
        );
    }
}
