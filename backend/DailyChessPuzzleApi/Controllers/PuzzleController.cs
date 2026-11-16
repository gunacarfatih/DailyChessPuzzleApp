
using System.Security.Claims;
using DailyChessPuzzleApi.DTOs;
using DailyChessPuzzleApi.Responses;
using DailyChessPuzzleApi.Services.Interfaces;
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
        [HttpGet("today")]
        public async Task<IActionResult> GetToday(
            [FromQuery] string? clientId,
            CancellationToken cancellationToken)
        {
            var identityError = ValidateIdentity(
                requireRegisteredUser: false,
                out var userId);

            if (identityError != null)
                return identityError;

            clientId = NormalizeClientId(clientId);

            var clientError = ValidateGuestClientId(userId, clientId);

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

        // Günlük bulmaca sonucunu gönderir.
        [HttpPost("submit")]
        public async Task<IActionResult> Submit(
            [FromBody] PuzzleSubmitRequest request,
            CancellationToken cancellationToken)
        {
            var identityError = ValidateIdentity(
                requireRegisteredUser: false,
                out var userId);

            if (identityError != null)
                return identityError;

            request.ClientId = NormalizeClientId(request.ClientId);

            var clientError = ValidateGuestClientId(
                userId,
                request.ClientId);

            if (clientError != null)
                return clientError;

            if (request.ClientId?.Length > 100)
            {
                return BadRequest(ResponseFactory.Failure<object>(
                    HttpContext,
                    "ClientId cannot exceed 100 characters.",
                    StatusCodes.Status400BadRequest,
                    "CLIENT_ID_TOO_LONG"));
            }

            if (request.CountryCode != null &&
                request.CountryCode.Length != 2)
            {
                return BadRequest(ResponseFactory.Failure<object>(
                    HttpContext,
                    "CountryCode must contain 2 characters.",
                    StatusCodes.Status400BadRequest,
                    "INVALID_COUNTRY_CODE"));
            }

            var response = await _puzzleService.SubmitAsync(
                request,
                userId,
                cancellationToken);

            return Ok(ResponseFactory.Success(
                HttpContext,
                "Puzzle submission completed successfully.",
                response));
        }

        // Günlük bulmaca oturumunu başlatır.
        [HttpPost("session/start")]
        public async Task<IActionResult> StartSession(
            [FromQuery] string? clientId,
            CancellationToken cancellationToken)
        {
            var identityError = ValidateIdentity(
                requireRegisteredUser: false,
                out var userId);

            if (identityError != null)
                return identityError;

            clientId = NormalizeClientId(clientId);

            var clientError = ValidateGuestClientId(userId, clientId);

            if (clientError != null)
                return clientError;

            if (clientId?.Length > 100)
            {
                return BadRequest(ResponseFactory.Failure<object>(
                    HttpContext,
                    "ClientId cannot exceed 100 characters.",
                    StatusCodes.Status400BadRequest,
                    "CLIENT_ID_TOO_LONG"));
            }

            var response = await _puzzleService.StartSessionAsync(
                userId,
                clientId,
                cancellationToken);

            return Ok(ResponseFactory.Success(
                HttpContext,
                "Puzzle session started successfully.",
                response));
        }

        // Günlük bulmacada hamle gönderir.
        [HttpPost("move")]
        public async Task<IActionResult> SubmitMove(
            [FromBody] PuzzleMoveRequest request,
            CancellationToken cancellationToken)
        {
            var identityError = ValidateIdentity(
                requireRegisteredUser: false,
                out var userId);

            if (identityError != null)
                return identityError;

            request.ClientId = NormalizeClientId(request.ClientId);

            var clientError = ValidateGuestClientId(
                userId,
                request.ClientId);

            if (clientError != null)
                return clientError;

            if (request.ClientId?.Length > 100)
            {
                return BadRequest(ResponseFactory.Failure<object>(
                    HttpContext,
                    "ClientId cannot exceed 100 characters.",
                    StatusCodes.Status400BadRequest,
                    "CLIENT_ID_TOO_LONG"));
            }

            var response = await _puzzleService.SubmitMoveAsync(
                request,
                userId,
                cancellationToken);

            return Ok(ResponseFactory.Success(
                HttpContext,
                "Puzzle move processed successfully.",
                response));
        }

        // Günlük bulmacada ipucu kullanır.
        [HttpPost("hint")]
        public async Task<IActionResult> UseHint(
            [FromQuery] string? clientId,
            CancellationToken cancellationToken)
        {
            var identityError = ValidateIdentity(
                requireRegisteredUser: false,
                out var userId);

            if (identityError != null)
                return identityError;

            clientId = NormalizeClientId(clientId);

            var clientError = ValidateGuestClientId(userId, clientId);

            if (clientError != null)
                return clientError;

            var response = await _puzzleService.UseHintAsync(
                userId,
                clientId,
                cancellationToken);

            return Ok(ResponseFactory.Success(
                HttpContext,
                "Puzzle hint used successfully.",
                response));
        }

        // Kullanıcının bonus bulmaca hakkını kontrol eder.
        [HttpGet("bonus")]
        public async Task<IActionResult> GetBonus(
            CancellationToken cancellationToken)
        {
            var identityError = ValidateIdentity(
                requireRegisteredUser: true,
                out var userId);

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

        // Bonus bulmacayı başlatır.
        [HttpPost("bonus/start")]
        public async Task<IActionResult> StartBonus(
            CancellationToken cancellationToken)
        {
            var identityError = ValidateIdentity(
                requireRegisteredUser: true,
                out var userId);

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

        // Bonus bulmacada hamle gönderir.
        [HttpPost("bonus/move")]
        public async Task<IActionResult> SubmitBonusMove(
            [FromBody] PuzzleMoveRequest request,
            CancellationToken cancellationToken)
        {
            var identityError = ValidateIdentity(
                requireRegisteredUser: true,
                out var userId);

            if (identityError != null)
                return identityError;

            var response = await _puzzleService.SubmitBonusMoveAsync(
                request,
                userId!.Value,
                cancellationToken);

            return Ok(ResponseFactory.Success(
                HttpContext,
                "Bonus puzzle move processed successfully.",
                response));
        }

        // Bonus bulmacada ipucu kullanır.
        [HttpPost("bonus/hint")]
        public async Task<IActionResult> UseBonusHint(
            CancellationToken cancellationToken)
        {
            var identityError = ValidateIdentity(
                requireRegisteredUser: true,
                out var userId);

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

        // Bonus bulmaca sonucunu kaydeder.
        [HttpPost("bonus/submit")]
        public async Task<IActionResult> SubmitBonus(
            CancellationToken cancellationToken)
        {
            var identityError = ValidateIdentity(
                requireRegisteredUser: true,
                out var userId);

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

        // Kimlik kontrolü.
        // Bonus işlemlerinde kayıtlı kullanıcı zorunludur.
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

        // Misafir kullanıcıların ClientId göndermesini zorunlu tutar.
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

        // ClientId değerini normalize eder.
        private static string? NormalizeClientId(string? clientId)
        {
            return string.IsNullOrWhiteSpace(clientId)
                ? null
                : clientId.Trim();
        }

        // JWT içerisindeki kullanıcı ID değerini alır.
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
