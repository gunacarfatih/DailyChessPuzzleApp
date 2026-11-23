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

        [HttpPost("verify-email")]
        [EnableRateLimiting("auth-verification")]
        public async Task<IActionResult> VerifyEmail(
            [FromBody] EmailVerificationRequest request,
            CancellationToken cancellationToken)
        {
            await _authService.VerifyEmailAsync(request, cancellationToken);
            return Ok(ResponseFactory.Success(
                HttpContext,
                "Email address verified successfully.",
                new { emailVerified = true }));
        }

        [HttpPost("verification/resend")]
        [EnableRateLimiting("auth-verification")]
        public async Task<IActionResult> ResendVerification(
            [FromBody] ResendVerificationRequest request,
            CancellationToken cancellationToken)
        {
            await _authService.ResendVerificationAsync(request, cancellationToken);
            return Ok(ResponseFactory.Success(
                HttpContext,
                "If the account needs verification, a new code has been sent.",
                new { accepted = true }));
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
