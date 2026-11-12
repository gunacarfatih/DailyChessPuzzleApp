
namespace DailyChessPuzzleApi.Models
{
    public class User
    {
        public int Id { get; set; }

        public string Username { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        // Parolanın kendisi değil, güvenli hash değeri saklanır.
        public string PasswordHash { get; set; } = string.Empty;

        // ISO 3166-1 alpha-2 ülke kodu (TR, DE, US vb.).
        public string? CountryCode { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAtUtc { get; set; }

        // Kullanıcının çözdüğü bulmaca denemeleri.
        public ICollection<PuzzleAttempt> PuzzleAttempts { get; set; }
            = new List<PuzzleAttempt>();
    }
}
