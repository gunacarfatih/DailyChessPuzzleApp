using DailyChessPuzzleApi.DTOs.Auth;
using DailyChessPuzzleApi.Responses;
using DailyChessPuzzleApi.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;

namespace DailyChessPuzzleApi.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;

        public AuthController(IAuthService authService)
        {
            _authService = authService;
        }

        [HttpPost("register")]
        [EnableRateLimiting("auth-register")]
        public async Task<IActionResult> Register(
            [FromBody] RegisterRequest request,
            CancellationToken cancellationToken)
        {
            var result = await _authService.RegisterAsync(
                request,
                cancellationToken);

            return StatusCode(
                StatusCodes.Status201Created,
                ResponseFactory.Success(
                    HttpContext,
                    "Registration completed successfully.",
                    result));
        }

        [Authorize]
        [HttpPost("verify-email")]
        [EnableRateLimiting("auth-verification")]
        public async Task<IActionResult> VerifyEmail(
            [FromBody] EmailVerificationRequest request,
            CancellationToken cancellationToken)
        {
            int userId = GetAuthenticatedUserId();

            await _authService.VerifyEmailAsync(
                userId,
                request,
                cancellationToken);

            return Ok(ResponseFactory.Success(
                HttpContext,
                "Email address verified successfully.",
                new { emailVerified = true }));
        }

        [Authorize]
        [HttpPost("verification/resend")]
        [EnableRateLimiting("auth-verification")]
        public async Task<IActionResult> ResendVerification(
            [FromBody] ResendVerificationRequest request,
            CancellationToken cancellationToken)
        {
            int userId = GetAuthenticatedUserId();

            await _authService.ResendVerificationAsync(
                userId,
                request,
                cancellationToken);

            return Ok(ResponseFactory.Success(
                HttpContext,
                "If the account needs verification, a new code has been sent.",
                new { accepted = true }));
        }

        [AllowAnonymous]
        [HttpPost("password/reset/request")]
        [EnableRateLimiting("auth-verification")]
        public async Task<IActionResult> RequestPasswordReset(
            [FromBody] PasswordResetCodeRequest request,
            CancellationToken cancellationToken)
        {
            await _authService.RequestPasswordResetAsync(request, cancellationToken);
            return Ok(ResponseFactory.Success(HttpContext,
                "If the account exists, a reset code has been sent.",
                new { accepted = true }));
        }

        [AllowAnonymous]
        [HttpPost("password/reset")]
        [EnableRateLimiting("auth-verification")]
        public async Task<IActionResult> ResetPassword(
            [FromBody] ResetPasswordRequest request,
            CancellationToken cancellationToken)
        {
            await _authService.ResetPasswordAsync(request, cancellationToken);
            return Ok(ResponseFactory.Success(HttpContext,
                "Password reset successfully.", new { reset = true }));
        }

        [HttpPost("login")]
        [EnableRateLimiting("auth-login")]
        public async Task<IActionResult> Login(
            [FromBody] LoginRequest request,
            CancellationToken cancellationToken)
        {
            var result = await _authService.LoginAsync(
                request,
                cancellationToken);

            return Ok(ResponseFactory.Success(
                HttpContext,
                "Login completed successfully.",
                result));
        }

        [AllowAnonymous]
        [HttpPost("refresh")]
        [EnableRateLimiting("auth-refresh")]
        public async Task<IActionResult> Refresh(
            [FromBody] RefreshTokenRequest request,
            CancellationToken cancellationToken)
        {
            var result = await _authService.RefreshAsync(request, cancellationToken);
            return Ok(ResponseFactory.Success(
                HttpContext,
                "Session refreshed successfully.",
                result));
        }

        [AllowAnonymous]
        [HttpPost("logout")]
        [EnableRateLimiting("auth-sensitive")]
        public async Task<IActionResult> Logout(
            [FromBody] RefreshTokenRequest request,
            CancellationToken cancellationToken)
        {
            await _authService.LogoutAsync(
                request,
                cancellationToken);

            return Ok(ResponseFactory.Success(
                HttpContext,
                "Session logged out successfully.",
                new { LoggedOut = true }));
        }

        [HttpPost("google")]
        [EnableRateLimiting("auth-google")]
        public async Task<IActionResult> GoogleLogin(
            [FromBody] GoogleLoginRequest request,
            CancellationToken cancellationToken)
        {
            var result = await _authService.GoogleLoginAsync(
                request,
                cancellationToken);

            return Ok(ResponseFactory.Success(
                HttpContext,
                "Google authentication completed successfully.",
                result));
        }

        [Authorize]
        [HttpPost("link-google")]
        [EnableRateLimiting("auth-sensitive")]
        public async Task<IActionResult> LinkGoogleAccount(
            [FromBody] LinkGoogleAccountRequest request,
            CancellationToken cancellationToken)
        {
            int userId = GetAuthenticatedUserId();
            await _authService.LinkGoogleAccountAsync(userId, request, cancellationToken);
            return Ok(ResponseFactory.Success(
                HttpContext,
                "Google account linked successfully.",
                new { linked = true }));
        }

        [Authorize]
        [HttpPost("set-password")]
        [EnableRateLimiting("auth-sensitive")]
        public async Task<IActionResult> SetPassword(
            [FromBody] SetPasswordRequest request,
            CancellationToken cancellationToken)
        {
            int userId = GetAuthenticatedUserId();

            await _authService.SetPasswordAsync(
                userId,
                request,
                cancellationToken);

            return Ok(ResponseFactory.Success(
                HttpContext,
                "Password has been set successfully.",
                new { HasPassword = true }));
        }

        [Authorize]
        [HttpPost("change-password")]
        [EnableRateLimiting("auth-sensitive")]
        public async Task<IActionResult> ChangePassword(
            [FromBody] ChangePasswordRequest request,
            CancellationToken cancellationToken)
        {
            int userId = GetAuthenticatedUserId();

            await _authService.ChangePasswordAsync(
                userId,
                request,
                cancellationToken);

            return Ok(ResponseFactory.Success(
                HttpContext,
                "Password has been changed successfully.",
                new { HasPassword = true }));
        }

        [Authorize]
        [HttpDelete("account")]
        [EnableRateLimiting("auth-account-delete")]
        public async Task<IActionResult> DeleteAccount(
            [FromBody] DeleteAccountRequest request,
            CancellationToken cancellationToken)
        {
            await _authService.DeleteAccountAsync(
                GetAuthenticatedUserId(), request, cancellationToken);
            return Ok(ResponseFactory.Success(
                HttpContext,
                "Account deleted successfully.",
                new { deleted = true }));
        }



        private int GetAuthenticatedUserId()
        {
            string? userIdValue = User.FindFirstValue(
                ClaimTypes.NameIdentifier);

            if (!int.TryParse(userIdValue, out int userId))
            {
                throw new UnauthorizedAccessException(
                    "INVALID_AUTHENTICATED_USER");
            }

            return userId;
        }
    }
}
