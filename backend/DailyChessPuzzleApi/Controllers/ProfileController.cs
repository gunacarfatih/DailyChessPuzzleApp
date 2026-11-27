using System.Security.Claims;
using DailyChessPuzzleApi.Data;
using DailyChessPuzzleApi.Exceptions;
using DailyChessPuzzleApi.Responses;
using DailyChessPuzzleApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace DailyChessPuzzleApi.Controllers;

[ApiController, Authorize, Route("api/profile")]
public sealed class ProfileController(ApplicationDbContext db) : ControllerBase
{
    private int CurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!int.TryParse(raw, out var id)) throw new AppException(401, "USER_ID_NOT_FOUND");
        return id;
    }

    [HttpGet]
    [EnableRateLimiting("leaderboard")]
    public async Task<IActionResult> Get(CancellationToken ct) => Ok(ResponseFactory.Success(
        HttpContext, "Profile progression retrieved successfully.",
        await ProfileProgression.GetAsync(db, CurrentUserId(), ct)));

    [HttpGet("history")]
    [EnableRateLimiting("leaderboard")]
    public async Task<IActionResult> History([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        page = Math.Clamp(page, 1, 10000); pageSize = Math.Clamp(pageSize, 1, 50);
        var userId = CurrentUserId();
        var query = db.PuzzleAttempts.AsNoTracking().Where(x => x.UserId == userId);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.SubmittedAtUtc)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new { x.Id, x.PuzzleId, x.PlayedOn, x.SubmittedAtUtc, x.Seconds,
                x.WrongMoves, x.UsedHint, x.Score, x.Puzzle!.Rating, x.Puzzle.Themes,
                Kind = db.BonusPuzzleAssignments.Any(b => b.PuzzleAttemptId == x.Id) ? "bonus" : "daily",
                Xp = db.ProfileProgress.Where(p => p.PuzzleAttemptId == x.Id).Select(p => (int?)p.Xp).FirstOrDefault() ?? 0 })
            .ToListAsync(ct);
        return Ok(ResponseFactory.Success(HttpContext, "Profile history retrieved successfully.",
            new { page, pageSize, total, items }));
    }

    [HttpGet("history/{attemptId:int}/watch")]
    [EnableRateLimiting("leaderboard")]
    public async Task<IActionResult> Watch(int attemptId, CancellationToken ct)
    {
        var userId = CurrentUserId();
        var puzzle = await db.PuzzleAttempts.AsNoTracking()
            .Where(x => x.Id == attemptId && x.UserId == userId)
            .Select(x => new { x.Id, x.PuzzleId, x.Puzzle!.Fen, x.Puzzle.Moves,
                x.Puzzle.Rating, x.Puzzle.Themes, x.PlayedOn, x.SubmittedAtUtc })
            .FirstOrDefaultAsync(ct);
        if (puzzle is null) throw new AppException(404, "PROFILE_ATTEMPT_NOT_FOUND");
        return Ok(ResponseFactory.Success(HttpContext,
            "Puzzle solution retrieved successfully.", puzzle));
    }
}
