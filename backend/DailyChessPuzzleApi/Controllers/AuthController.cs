
using DailyChessPuzzleApi.DTOs.Auth;
using DailyChessPuzzleApi.Responses;
using DailyChessPuzzleApi.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

        [HttpPost("login")]
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

        [HttpPost("google")]
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
        [HttpPost("set-password")]
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
