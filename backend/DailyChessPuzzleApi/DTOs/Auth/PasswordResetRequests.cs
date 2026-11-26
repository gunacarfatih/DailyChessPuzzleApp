using System.ComponentModel.DataAnnotations;

namespace DailyChessPuzzleApi.DTOs.Auth;

public sealed class PasswordResetCodeRequest
{
    [Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = string.Empty;
}

public sealed class ResetPasswordRequest
{
    [Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = string.Empty;

    [Required, RegularExpression("^[0-9]{6}$")]
    public string Code { get; set; } = string.Empty;

    [Required, StringLength(128, MinimumLength = 8)]
    public string NewPassword { get; set; } = string.Empty;
}
