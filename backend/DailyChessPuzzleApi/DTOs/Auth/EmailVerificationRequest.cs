using System.ComponentModel.DataAnnotations;

namespace DailyChessPuzzleApi.DTOs.Auth;

public sealed class EmailVerificationRequest
{
    [Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = string.Empty;

    [Required, RegularExpression("^[0-9]{6}$")]
    public string Code { get; set; } = string.Empty;
}

public sealed class ResendVerificationRequest
{
    [Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = string.Empty;
}

public sealed class RegistrationResponse
{
    public string Email { get; set; } = string.Empty;

    public bool EmailVerificationRequired { get; set; } = true;
}
