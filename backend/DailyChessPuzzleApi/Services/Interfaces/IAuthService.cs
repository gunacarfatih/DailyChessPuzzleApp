
using DailyChessPuzzleApi.DTOs.Auth;

namespace DailyChessPuzzleApi.Services.Interfaces
{
    public interface IAuthService
    {
        Task<RegistrationResponse> RegisterAsync(
            RegisterRequest request,
            CancellationToken cancellationToken);

        Task VerifyEmailAsync(
            EmailVerificationRequest request,
            CancellationToken cancellationToken);

        Task ResendVerificationAsync(
            ResendVerificationRequest request,
            CancellationToken cancellationToken);

        Task<AuthResponse> LoginAsync(
            LoginRequest request,
            CancellationToken cancellationToken);

        Task<AuthResponse> RefreshAsync(
            RefreshTokenRequest request,
            CancellationToken cancellationToken);

        Task LogoutAsync(
            RefreshTokenRequest request,
            CancellationToken cancellationToken);

        Task<AuthResponse> GoogleLoginAsync(
            GoogleLoginRequest request,
            CancellationToken cancellationToken);

        Task LinkGoogleAccountAsync(
            int userId,
            LinkGoogleAccountRequest request,
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
