
namespace DailyChessPuzzleApi.Models
{
    public class User
    {
        public int Id { get; set; }

        public string Username { get; set; } = string.Empty;

        public string FirstName { get; set; } = string.Empty;

        public string LastName { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        // Normal kayıt olan kullanıcılarda parola hash'i bulunur.
        // Google ile kayıt olan kullanıcılarda null olur.
        public string? PasswordHash { get; set; }

        // Google hesabının benzersiz kullanıcı kimliği (sub).
        public string? GoogleId { get; set; }

        // ISO 3166-1 alpha-2 ülke kodu (TR, DE, US vb.).
        public string? CountryCode { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAtUtc { get; set; }

        // Kullanıcının çözdüğü bulmaca denemeleri.
        public ICollection<PuzzleAttempt> PuzzleAttempts { get; set; }
            = new List<PuzzleAttempt>();
    }
}
