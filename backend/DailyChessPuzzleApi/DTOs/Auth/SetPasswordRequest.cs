
using System.ComponentModel.DataAnnotations;

namespace DailyChessPuzzleApi.DTOs.Auth
{
    public class SetPasswordRequest
    {
        [Required]
        [StringLength(128, MinimumLength = 8)]
        public string NewPassword { get; set; } = string.Empty;
    }
}
