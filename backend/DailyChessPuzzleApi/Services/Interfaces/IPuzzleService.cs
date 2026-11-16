
using DailyChessPuzzleApi.DTOs;

namespace DailyChessPuzzleApi.Services.Interfaces
{
    public interface IPuzzleService
    {
        // Günün bulmacasını getirir.
        Task<PuzzleTodayResponse?> GetTodayAsync(
            int? userId,
            string? clientId,
            CancellationToken cancellationToken = default);

        // Ana bulmaca sonucunu kaydeder.
        Task<PuzzleSubmitResponse> SubmitAsync(
            PuzzleSubmitRequest request,
            int? userId,
            CancellationToken cancellationToken = default);

        // Ana bulmaca oturumunu başlatır veya mevcut oturumu getirir.
        Task<PuzzleSessionResponse> StartSessionAsync(
            int? userId,
            string? clientId,
            CancellationToken cancellationToken = default);

        // Ana bulmacada gönderilen hamleyi kontrol eder.
        Task<PuzzleSessionResponse> SubmitMoveAsync(
            PuzzleMoveRequest request,
            int? userId,
            CancellationToken cancellationToken = default);

        // Ana bulmaca için ipucu kullanır.
        Task<PuzzleSessionResponse> UseHintAsync(
            int? userId,
            string? clientId,
            CancellationToken cancellationToken = default);

        // Kullanıcının bonus hakkını ve bonus bulmacasını getirir.
        Task<BonusPuzzleResponse> GetBonusAsync(
            int userId,
            CancellationToken cancellationToken = default);

        // Bonus bulmacayı başlatır ve 30 saniyelik süreyi başlatır.
        Task<BonusPuzzleResponse> StartBonusAsync(
            int userId,
            CancellationToken cancellationToken = default);

        // Bonus bulmacada gönderilen hamleyi kontrol eder.
        Task<PuzzleSessionResponse> SubmitBonusMoveAsync(
            PuzzleMoveRequest request,
            int userId,
            CancellationToken cancellationToken = default);

        // Bonus bulmacada ipucu kullanır.
        Task<PuzzleSessionResponse> UseBonusHintAsync(
            int userId,
            CancellationToken cancellationToken = default);

        // Bonus sonucunu hesaplar ve kaydeder.
        Task<BonusPuzzleSubmitResponse> SubmitBonusAsync(
            int userId,
            CancellationToken cancellationToken = default);
    }
}
