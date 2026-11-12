
using DailyChessPuzzleApi.DTOs;

namespace DailyChessPuzzleApi.Services.Interfaces
{
    public interface IPuzzleService
    {
        // Günün bulmacasını getirir.
        // Kayıtlı kullanıcı UserId, misafir ClientId üzerinden tanınır.
        Task<PuzzleTodayResponse?> GetTodayAsync(
            int? userId,
            string? clientId,
            CancellationToken cancellationToken = default
        );

        // Bulmaca sonucunu kaydeder.
        // Kayıtlı kullanıcı kimliği JWT üzerinden alınır.
        Task<PuzzleSubmitResponse> SubmitAsync(
            PuzzleSubmitRequest request,
            int? userId,
            CancellationToken cancellationToken = default
        );
    }
}
