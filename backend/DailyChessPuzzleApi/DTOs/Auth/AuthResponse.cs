
namespace DailyChessPuzzleApi.DTOs.Auth
{
    public class AuthResponse
    {
        public int UserId { get; set; }

        public string Username { get; set; } = string.Empty;

        public string FirstName { get; set; } = string.Empty;

        public string LastName { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public string? CountryCode { get; set; }

        public bool HasPassword { get; set; }

        public string AccessToken { get; set; } = string.Empty;

        public DateTime ExpiresAtUtc { get; set; }
    }
}
