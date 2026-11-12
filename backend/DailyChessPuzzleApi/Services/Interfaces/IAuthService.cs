
using DailyChessPuzzleApi.DTOs.Auth;

namespace DailyChessPuzzleApi.Services.Interfaces
{
    public interface IAuthService
    {
        Task<AuthResponse> RegisterAsync(
            RegisterRequest request,
            CancellationToken cancellationToken);

        Task<AuthResponse> LoginAsync(
            LoginRequest request,
            CancellationToken cancellationToken);

        Task<AuthResponse> GoogleLoginAsync(
            GoogleLoginRequest request,
            CancellationToken cancellationToken);

        Task SetPasswordAsync(
            int userId,
            SetPasswordRequest request,
            CancellationToken cancellationToken);

        Task ChangePasswordAsync(
            int userId,
            ChangePasswordRequest request,
            CancellationToken cancellationToken);
    }
}
