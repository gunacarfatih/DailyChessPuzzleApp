
using System.Security.Claims;
using DailyChessPuzzleApi.DTOs;
using DailyChessPuzzleApi.Responses;
using DailyChessPuzzleApi.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DailyChessPuzzleApi.Controllers
{
    [ApiController]
    [Route("api/puzzle")]
    public class PuzzleController : ControllerBase
    {
        private readonly IPuzzleService _puzzleService;

        public PuzzleController(IPuzzleService puzzleService)
        {
            _puzzleService = puzzleService;
        }

        // ---------------------------------------------------------
        // GÜNLÜK BULMACA
        // ---------------------------------------------------------

        [HttpGet("today")]
        [EnableRateLimiting("puzzle-read")]
        public async Task<IActionResult> GetToday(
            [FromQuery] string? clientId,
            CancellationToken cancellationToken)
        {
            var identityError = ValidateIdentity(false, out var userId);

            if (identityError != null)
                return identityError;

            clientId = NormalizeClientId(clientId);

            var clientError = ValidateGuestClientId(userId, clientId)
                ?? ValidateClientIdLength(clientId);

            if (clientError != null)
                return clientError;

            var puzzle = await _puzzleService.GetTodayAsync(
                userId,
                clientId,
                cancellationToken);

            if (puzzle == null)
            {
                return NotFound(ResponseFactory.Failure<object>(
                    HttpContext,
                    "Today's puzzle was not found.",
                    StatusCodes.Status404NotFound,
                    "PUZZLE_NOT_FOUND"));
            }

            return Ok(ResponseFactory.Success(
                HttpContext,
                "Today's puzzle retrieved successfully.",
                puzzle));
        }

        // ---------------------------------------------------------
        // YENİ GÜNLÜK OTURUM
        // Her çağrıda yeni bir oturum oluşturulur.
        // ---------------------------------------------------------

        [HttpPost("session/start")]
        [EnableRateLimiting("puzzle-write")]
        public async Task<IActionResult> StartSession(
            [FromQuery] string? clientId,
            CancellationToken cancellationToken)
        {
            var identityError = ValidateIdentity(false, out var userId);

            if (identityError != null)
                return identityError;

            clientId = NormalizeClientId(clientId);

            var clientError = ValidateGuestClientId(userId, clientId)
                ?? ValidateClientIdLength(clientId);

            if (clientError != null)
                return clientError;

            var response = await _puzzleService.StartSessionAsync(
                userId,
                clientId,
                cancellationToken);

            return Ok(ResponseFactory.Success(
                HttpContext,
                "Puzzle session started successfully.",
                response));
        }

        // ---------------------------------------------------------
        // GÜNLÜK BULMACA HAMLESİ
        // SessionId zorunludur.
        // ---------------------------------------------------------

        [HttpPost("move")]
        [EnableRateLimiting("puzzle-move")]
        public async Task<IActionResult> SubmitMove(
            [FromBody] PuzzleMoveRequest request,
            CancellationToken cancellationToken)
        {
            var identityError = ValidateIdentity(false, out var userId);

            if (identityError != null)
                return identityError;

            request.ClientId = NormalizeClientId(request.ClientId);

            var clientError = ValidateGuestClientId(
                userId,
                request.ClientId)
                ?? ValidateClientIdLength(request.ClientId);

            if (clientError != null)
                return clientError;

            if (request.SessionId == null || request.SessionId <= 0)
            {
                return BadRequest(ResponseFactory.Failure<object>(
                    HttpContext,
                    "A valid SessionId is required.",
                    StatusCodes.Status400BadRequest,
                    "SESSION_ID_REQUIRED"));
            }

            if (string.IsNullOrWhiteSpace(request.Uci) ||
                request.Uci.Length < 4 ||
                request.Uci.Length > 10)
            {
                return BadRequest(ResponseFactory.Failure<object>(
                    HttpContext,
                    "Invalid UCI move.",
                    StatusCodes.Status400BadRequest,
                    "INVALID_UCI"));
            }

            request.Uci = request.Uci.Trim();

            var response = await _puzzleService.SubmitMoveAsync(
                request,
                userId,
                cancellationToken);

            return Ok(ResponseFactory.Success(
                HttpContext,
                "Puzzle move processed successfully.",
                response));
        }

        // ---------------------------------------------------------
        // GÜNLÜK BULMACA İPUCU
        // SessionId zorunludur.
        // ---------------------------------------------------------

        [HttpPost("hint")]
        [EnableRateLimiting("puzzle-write")]
        public async Task<IActionResult> UseHint(
            [FromQuery] int? sessionId,
            [FromQuery] string? clientId,
            CancellationToken cancellationToken)
        {
            var identityError = ValidateIdentity(false, out var userId);

            if (identityError != null)
                return identityError;

            clientId = NormalizeClientId(clientId);

            var clientError = ValidateGuestClientId(userId, clientId)
                ?? ValidateClientIdLength(clientId);

            if (clientError != null)
                return clientError;

            if (sessionId == null || sessionId <= 0)
            {
                return BadRequest(ResponseFactory.Failure<object>(
                    HttpContext,
                    "A valid SessionId is required.",
                    StatusCodes.Status400BadRequest,
                    "SESSION_ID_REQUIRED"));
            }

            var response = await _puzzleService.UseHintAsync(
                sessionId.Value,
                userId,
                clientId,
                cancellationToken);

            return Ok(ResponseFactory.Success(
                HttpContext,
                "Puzzle hint used successfully.",
                response));
        }

        // ---------------------------------------------------------
        // BONUS BULMACA
        // ---------------------------------------------------------

        [HttpGet("bonus")]
        [EnableRateLimiting("puzzle-read")]
        public async Task<IActionResult> GetBonus(
            CancellationToken cancellationToken)
        {
            var identityError = ValidateIdentity(true, out var userId);

            if (identityError != null)
                return identityError;

            var response = await _puzzleService.GetBonusAsync(
                userId!.Value,
                cancellationToken);

            return Ok(ResponseFactory.Success(
                HttpContext,
                "Bonus puzzle status retrieved successfully.",
                response));
        }

        [HttpPost("bonus/start")]
        [EnableRateLimiting("puzzle-write")]
        public async Task<IActionResult> StartBonus(
            CancellationToken cancellationToken)
        {
            var identityError = ValidateIdentity(true, out var userId);

            if (identityError != null)
                return identityError;

            var response = await _puzzleService.StartBonusAsync(
                userId!.Value,
                cancellationToken);

            return Ok(ResponseFactory.Success(
                HttpContext,
                "Bonus puzzle started successfully.",
                response));
        }

        [HttpPost("bonus/move")]
        [EnableRateLimiting("puzzle-move")]
        public async Task<IActionResult> SubmitBonusMove(
            [FromBody] PuzzleMoveRequest request,
            CancellationToken cancellationToken)
        {
            var identityError = ValidateIdentity(true, out var userId);

            if (identityError != null)
                return identityError;

            if (string.IsNullOrWhiteSpace(request.Uci) ||
                request.Uci.Length < 4 ||
                request.Uci.Length > 10)
            {
                return BadRequest(ResponseFactory.Failure<object>(
                    HttpContext,
                    "Invalid UCI move.",
                    StatusCodes.Status400BadRequest,
                    "INVALID_UCI"));
            }

            request.Uci = request.Uci.Trim();
            request.ClientId = null;

            var response = await _puzzleService.SubmitBonusMoveAsync(
                request,
                userId!.Value,
                cancellationToken);

            return Ok(ResponseFactory.Success(
                HttpContext,
                "Bonus puzzle move processed successfully.",
                response));
        }

        [HttpPost("bonus/hint")]
        [EnableRateLimiting("puzzle-write")]
        public async Task<IActionResult> UseBonusHint(
            CancellationToken cancellationToken)
        {
            var identityError = ValidateIdentity(true, out var userId);

            if (identityError != null)
                return identityError;

            var response = await _puzzleService.UseBonusHintAsync(
                userId!.Value,
                cancellationToken);

            return Ok(ResponseFactory.Success(
                HttpContext,
                "Bonus puzzle hint used successfully.",
                response));
        }

        [HttpPost("bonus/submit")]
        [EnableRateLimiting("puzzle-write")]
        public async Task<IActionResult> SubmitBonus(
            CancellationToken cancellationToken)
        {
            var identityError = ValidateIdentity(true, out var userId);

            if (identityError != null)
                return identityError;

            var response = await _puzzleService.SubmitBonusAsync(
                userId!.Value,
                cancellationToken);

            return Ok(ResponseFactory.Success(
                HttpContext,
                "Bonus puzzle submission completed successfully.",
                response));
        }

        // ---------------------------------------------------------
        // VALIDATION
        // ---------------------------------------------------------

        private IActionResult? ValidateIdentity(
            bool requireRegisteredUser,
            out int? userId)
        {
            userId = GetAuthenticatedUserId();

            bool isAuthenticated =
                User.Identity?.IsAuthenticated == true;

            if (!isAuthenticated && requireRegisteredUser)
            {
                return Unauthorized(ResponseFactory.Failure<object>(
                    HttpContext,
                    "Authentication is required for bonus puzzles.",
                    StatusCodes.Status401Unauthorized,
                    "AUTHENTICATION_REQUIRED"));
            }

            if (isAuthenticated && userId == null)
            {
                return Unauthorized(ResponseFactory.Failure<object>(
                    HttpContext,
                    "Authenticated user identity could not be determined.",
                    StatusCodes.Status401Unauthorized,
                    "USER_ID_NOT_FOUND"));
            }

            return null;
        }

        private IActionResult? ValidateGuestClientId(
            int? userId,
            string? clientId)
        {
            if (userId == null && clientId == null)
            {
                return BadRequest(ResponseFactory.Failure<object>(
                    HttpContext,
                    "ClientId is required for guest users.",
                    StatusCodes.Status400BadRequest,
                    "CLIENT_ID_REQUIRED"));
            }

            return null;
        }

        private IActionResult? ValidateClientIdLength(string? clientId)
        {
            if (clientId?.Length > 100)
            {
                return BadRequest(ResponseFactory.Failure<object>(
                    HttpContext,
                    "ClientId cannot exceed 100 characters.",
                    StatusCodes.Status400BadRequest,
                    "CLIENT_ID_TOO_LONG"));
            }

            return null;
        }

        private static string? NormalizeClientId(string? clientId)
        {
            return string.IsNullOrWhiteSpace(clientId)
                ? null
                : clientId.Trim();
        }

        private int? GetAuthenticatedUserId()
        {
            if (User.Identity?.IsAuthenticated != true)
                return null;

            var userIdClaim =
                User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User.FindFirstValue("sub");

            return int.TryParse(userIdClaim, out var userId)
                ? userId
                : null;
        }
    }
}
