
using System.ComponentModel.DataAnnotations;

namespace DailyChessPuzzleApi.DTOs.Auth
{
    public class ChangePasswordRequest
    {
        [Required]
        [StringLength(128, MinimumLength = 8)]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required]
        [StringLength(128, MinimumLength = 8)]
        public string NewPassword { get; set; } = string.Empty;
    }
}
