
using System.ComponentModel.DataAnnotations;

namespace DailyChessPuzzleApi.DTOs.Auth
{
    public class RegisterRequest
    {
        [Required]
        [StringLength(50, MinimumLength = 3)]
        public string Username { get; set; } = string.Empty;

        [Required]
        [StringLength(50, MinimumLength = 1)]
        public string FirstName { get; set; } = string.Empty;

        [Required]
        [StringLength(50, MinimumLength = 1)]
        public string LastName { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        [StringLength(254)]
        public string Email { get; set; } = string.Empty;

        [Required]
        [StringLength(128, MinimumLength = 8)]
        public string Password { get; set; } = string.Empty;

        [StringLength(2, MinimumLength = 2)]
        public string? CountryCode { get; set; }

        [Required]
        [StringLength(100, MinimumLength = 1)]
        public string ClientId { get; set; } = string.Empty;
    }
}
