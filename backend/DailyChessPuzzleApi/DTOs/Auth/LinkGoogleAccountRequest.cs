using System.ComponentModel.DataAnnotations;

namespace DailyChessPuzzleApi.DTOs.Auth;

public sealed class LinkGoogleAccountRequest
{
    [Required]
    public string IdToken { get; set; } = string.Empty;

    [Required]
    public string CurrentPassword { get; set; } = string.Empty;
}
