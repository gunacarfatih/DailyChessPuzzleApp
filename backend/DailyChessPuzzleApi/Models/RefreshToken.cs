namespace DailyChessPuzzleApi.Models;

public sealed class RefreshToken
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    // Aynı giriş oturumunda döndürülen bütün yenilenmiş
    // token'lar aynı aile kimliğini taşır.
    public Guid FamilyId { get; set; } = Guid.NewGuid();

    // Bu token yenilendiğinde yerine oluşturulan token'ın hash'i.
    public string? ReplacedByTokenHash { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime ExpiresAtUtc { get; set; }

    public DateTime? RevokedAtUtc { get; set; }
}