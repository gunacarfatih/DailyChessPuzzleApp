using System.ComponentModel.DataAnnotations;
namespace DailyChessPuzzleApi.DTOs.Auth;
public sealed class DeleteAccountRequest
{
    [StringLength(1024)] public string? CurrentPassword { get; set; }
    [StringLength(8192)] public string? GoogleIdToken { get; set; }
}
