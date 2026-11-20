using System.ComponentModel.DataAnnotations;

namespace DailyChessPuzzleApi.DTOs.Auth;

public sealed class RefreshTokenRequest
{
    [Required, StringLength(128, MinimumLength = 32)]
    public string RefreshToken { get; set; } = string.Empty;
}
