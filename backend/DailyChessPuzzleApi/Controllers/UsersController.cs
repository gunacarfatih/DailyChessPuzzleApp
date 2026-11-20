using System.Security.Claims;
using DailyChessPuzzleApi.Data;
using DailyChessPuzzleApi.DTOs;
using DailyChessPuzzleApi.Exceptions;
using DailyChessPuzzleApi.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DailyChessPuzzleApi.Controllers;

[ApiController]
[Authorize]
[Route("api/users")]
public sealed class UsersController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public UsersController(ApplicationDbContext context) => _context = context;

    [HttpPatch("me")]
    public async Task<IActionResult> UpdateCountry(
        [FromBody] UpdateCountryRequest request,
        CancellationToken cancellationToken)
    {
        var rawId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!int.TryParse(rawId, out var userId))
            throw new AppException(401, "USER_ID_NOT_FOUND");

        var user = await _context.Users.FirstOrDefaultAsync(
            x => x.Id == userId,
            cancellationToken);
        if (user == null)
            throw new AppException(404, "USER_NOT_FOUND");

        user.CountryCode = string.IsNullOrWhiteSpace(request.CountryCode)
            ? null
            : request.CountryCode.Trim().ToUpperInvariant();
        user.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        return Ok(ResponseFactory.Success(
            HttpContext,
            "Country updated successfully.",
            new { countryCode = user.CountryCode }));
    }
}
