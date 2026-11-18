
using DailyChessPuzzleApi.DTOs;

namespace DailyChessPuzzleApi.Services.Interfaces
{
    public interface IPuzzleService
    {
        Task<PuzzleTodayResponse?> GetTodayAsync(
            int? userId,
            string? clientId,
            CancellationToken cancellationToken = default);

        Task<PuzzleSessionResponse> StartSessionAsync(
            int? userId,
            string? clientId,
            CancellationToken cancellationToken = default);

        Task<PuzzleSessionResponse> SubmitMoveAsync(
            PuzzleMoveRequest request,
            int? userId,
            CancellationToken cancellationToken = default);

        Task<PuzzleSessionResponse> UseHintAsync(
            int sessionId,
            int? userId,
            string? clientId,
            CancellationToken cancellationToken = default);

        Task<BonusPuzzleResponse> GetBonusAsync(
            int userId,
            CancellationToken cancellationToken = default);

        Task<BonusPuzzleResponse> StartBonusAsync(
            int userId,
            CancellationToken cancellationToken = default);

        Task<PuzzleSessionResponse> SubmitBonusMoveAsync(
            PuzzleMoveRequest request,
            int userId,
            CancellationToken cancellationToken = default);

        Task<PuzzleSessionResponse> UseBonusHintAsync(
            int userId,
            CancellationToken cancellationToken = default);

        Task<BonusPuzzleSubmitResponse> SubmitBonusAsync(
            int userId,
            CancellationToken cancellationToken = default);
    }
}
