
using System.Security.Claims;
using DailyChessPuzzleApi.DTOs;
using DailyChessPuzzleApi.Services.Interfaces;
using DailyChessPuzzleApi.Responses;
using Microsoft.AspNetCore.Mvc;

namespace DailyChessPuzzleApi.Controllers
{
    [ApiController]
    [Route("api/puzzle")]
    public class PuzzleController : ControllerBase
    {
        private readonly IPuzzleService _puzzleService;
        private readonly ILogger<PuzzleController> _logger;

        public PuzzleController(
            IPuzzleService puzzleService,
            ILogger<PuzzleController> logger)
        {
            _puzzleService = puzzleService;
            _logger = logger;
        }

        // Günün bulmacasını getirir.
        // Kayıtlı kullanıcı JWT, misafir ClientId üzerinden tanınır.
        [HttpGet("today")]
        public async Task<IActionResult> GetToday(
            [FromQuery] string? clientId,
            CancellationToken cancellationToken)
        {
            var userId = GetAuthenticatedUserId();

            clientId = string.IsNullOrWhiteSpace(clientId)
                ? null
                : clientId.Trim();

            bool isAuthenticated = User.Identity?.IsAuthenticated == true;

            // Misafir kullanıcılar ClientId göndermek zorundadır.
            if (!isAuthenticated && clientId == null)
            {
                _logger.LogWarning(
                    "GET today's puzzle request rejected. Guest ClientId is missing.");

                return BadRequest(ResponseFactory.Failure<object>(
                    HttpContext,
                    "ClientId is required for guest users.",
                    StatusCodes.Status400BadRequest,
                    "CLIENT_ID_REQUIRED"
                ));
            }

            // Giriş yapmış kullanıcının JWT kimliği geçerli olmalıdır.
            if (isAuthenticated && userId == null)
            {
                return Unauthorized(ResponseFactory.Failure<object>(
                    HttpContext,
                    "Authenticated user identity could not be determined.",
                    StatusCodes.Status401Unauthorized,
                    "USER_ID_NOT_FOUND"
                ));
            }

            _logger.LogInformation(
                "GET today's puzzle request received. UserId: {UserId}, ClientId: {ClientId}",
                userId,
                clientId
            );

            var puzzle = await _puzzleService.GetTodayAsync(
                userId,
                clientId,
                cancellationToken
            );

            if (puzzle == null)
            {
                return NotFound(ResponseFactory.Failure<object>(
                    HttpContext,
                    "Today's puzzle was not found.",
                    StatusCodes.Status404NotFound,
                    "PUZZLE_NOT_FOUND"
                ));
            }

            return Ok(ResponseFactory.Success(
                HttpContext,
                "Today's puzzle retrieved successfully.",
                puzzle
            ));
        }

        // Bulmaca sonucunu gönderir.
        [HttpPost("submit")]
        public async Task<IActionResult> Submit(
            [FromBody] PuzzleSubmitRequest request,
            CancellationToken cancellationToken)
        {
            var userId = GetAuthenticatedUserId();

            request.ClientId = string.IsNullOrWhiteSpace(request.ClientId)
                ? null
                : request.ClientId.Trim();

            bool isAuthenticated = User.Identity?.IsAuthenticated == true;

            // Misafir kullanıcılar ClientId göndermek zorundadır.
            if (!isAuthenticated && request.ClientId == null)
            {
                _logger.LogWarning(
                    "Puzzle submission rejected. Guest ClientId is missing.");

                return BadRequest(ResponseFactory.Failure<object>(
                    HttpContext,
                    "ClientId is required for guest users.",
                    StatusCodes.Status400BadRequest,
                    "CLIENT_ID_REQUIRED"
                ));
            }

            if (isAuthenticated && userId == null)
            {
                return Unauthorized(ResponseFactory.Failure<object>(
                    HttpContext,
                    "Authenticated user identity could not be determined.",
                    StatusCodes.Status401Unauthorized,
                    "USER_ID_NOT_FOUND"
                ));
            }

            if (request.ClientId?.Length > 100)
            {
                return BadRequest(ResponseFactory.Failure<object>(
                    HttpContext,
                    "ClientId cannot exceed 100 characters.",
                    StatusCodes.Status400BadRequest,
                    "CLIENT_ID_TOO_LONG"
                ));
            }

            if (request.CountryCode != null &&
                request.CountryCode.Length != 2)
            {
                return BadRequest(ResponseFactory.Failure<object>(
                    HttpContext,
                    "CountryCode must contain 2 characters.",
                    StatusCodes.Status400BadRequest,
                    "INVALID_COUNTRY_CODE"
                ));
            }

            _logger.LogInformation(
                "Puzzle submission request received. PuzzleId: {PuzzleId}, UserId: {UserId}, ClientId: {ClientId}",
                request.PuzzleId,
                userId,
                request.ClientId
            );

            var response = await _puzzleService.SubmitAsync(
                request,
                userId,
                cancellationToken
            );

            return Ok(ResponseFactory.Success(
                HttpContext,
                "Puzzle submission completed successfully.",
                response
            ));
        }

        // JWT içerisindeki kullanıcı ID değerini alır.
        private int? GetAuthenticatedUserId()
        {
            if (User.Identity?.IsAuthenticated != true)
            {
                return null;
            }

            var userIdClaim =
                User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User.FindFirstValue("sub");

            return int.TryParse(userIdClaim, out var userId)
                ? userId
                : null;
        }
    }
}
