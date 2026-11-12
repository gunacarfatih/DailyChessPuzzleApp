
using DailyChessPuzzleApi.DTOs.Auth;
using DailyChessPuzzleApi.Responses;
using DailyChessPuzzleApi.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

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
    }
}
