
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
    }
}
