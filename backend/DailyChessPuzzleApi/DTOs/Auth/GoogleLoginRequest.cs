
using System.ComponentModel.DataAnnotations;

namespace DailyChessPuzzleApi.DTOs.Auth
{
    public class GoogleLoginRequest
    {
        [Required]
        public string IdToken { get; set; } = string.Empty;
    }
}
