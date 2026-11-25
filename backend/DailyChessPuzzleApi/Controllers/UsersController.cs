
using System.Security.Claims;
using DailyChessPuzzleApi.Data;
using DailyChessPuzzleApi.DTOs;
using DailyChessPuzzleApi.Exceptions;
using DailyChessPuzzleApi.Helpers;
using DailyChessPuzzleApi.Responses;
using DailyChessPuzzleApi.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace DailyChessPuzzleApi.Controllers;

[ApiController]
[Authorize]
[Route("api/users")]
public sealed class UsersController : ControllerBase
{
    private static readonly TimeSpan CountryChangeInterval =
        TimeSpan.FromHours(24);

    private readonly ApplicationDbContext _context;
    private readonly ILeaderboardService _leaderboardService;

    public UsersController(
        ApplicationDbContext context,
        ILeaderboardService leaderboardService)
    {
        _context = context;
        _leaderboardService = leaderboardService;
    }

    [HttpGet("me/stats")]
    [EnableRateLimiting("leaderboard")]
    public async Task<IActionResult> GetMyStats(
        CancellationToken cancellationToken)
    {
        var rawId =
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (!int.TryParse(rawId, out var userId))
            throw new AppException(401, "USER_ID_NOT_FOUND");

        var stats = await _leaderboardService.GetUserStatsAsync(
            userId,
            cancellationToken);

        return Ok(ResponseFactory.Success(
            HttpContext,
            "User statistics retrieved successfully.",
            stats));
    }

    [HttpPatch("me")]
    [EnableRateLimiting("auth-sensitive")]
    public async Task<IActionResult> UpdateCountry(
        [FromBody] UpdateCountryRequest request,
        CancellationToken cancellationToken)
    {
        var rawId =
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (!int.TryParse(rawId, out var userId))
            throw new AppException(401, "USER_ID_NOT_FOUND");

        var requestedCountry =
            CountryCodeHelper.Normalize(request.CountryCode);

        if (requestedCountry is null)
            throw new AppException(400, "INVALID_COUNTRY_CODE");

        await using var transaction =
            await _context.Database.BeginTransactionAsync(
                cancellationToken);

        // Kullanıcı satırını transaction sonuna kadar kilitle.
        var users = await _context.Users
            .FromSqlInterpolated($"""
                SELECT *
                FROM "Users"
                WHERE "Id" = {userId}
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);

        var user = users.SingleOrDefault();

        if (user is null)
            throw new AppException(404, "USER_NOT_FOUND");

        // Daha önce izlenen entity varsa güncel veriyi yükle.
        await _context.Entry(user)
            .ReloadAsync(cancellationToken);

        var now = DateTime.UtcNow;

        // Aynı ülkenin tekrar bildirilmesi değişiklik sayılmaz.
        if (string.Equals(
                user.CountryCode,
                requestedCountry,
                StringComparison.Ordinal))
        {
            await transaction.CommitAsync(cancellationToken);

            return Ok(ResponseFactory.Success(
                HttpContext,
                "Country is already up to date.",
                new
                {
                    countryCode = user.CountryCode,
                    countryChangedAtUtc = user.CountryChangedAtUtc,
                    nextCountryChangeAtUtc =
                        user.CountryChangedAtUtc?.Add(
                            CountryChangeInterval),
                    countryChanged = false
                }));
        }

        // İlk ülke atamasında bekleme süresi uygulanmaz.
        // Geçmiş değişiklik tarihi bilinmeyen eski kullanıcılar
        // da ilk güncellemelerini beklemeden yapabilir.
        if (!string.IsNullOrWhiteSpace(user.CountryCode) &&
            user.CountryChangedAtUtc.HasValue)
        {
            var nextAllowedChange =
                user.CountryChangedAtUtc.Value.Add(
                    CountryChangeInterval);

            if (now < nextAllowedChange)
            {
                throw new AppException(
                    409,
                    "COUNTRY_CHANGE_COOLDOWN");
            }
        }

        user.CountryCode = requestedCountry;
        user.CountryChangedAtUtc = now;
        user.UpdatedAtUtc = now;

        await _context.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return Ok(ResponseFactory.Success(
            HttpContext,
            "Country updated successfully.",
            new
            {
                countryCode = user.CountryCode,
                countryChangedAtUtc = user.CountryChangedAtUtc,
                nextCountryChangeAtUtc =
                    now.Add(CountryChangeInterval),
                countryChanged = true
            }));
    }
}
