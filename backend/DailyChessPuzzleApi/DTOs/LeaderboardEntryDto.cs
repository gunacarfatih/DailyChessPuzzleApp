
namespace DailyChessPuzzleApi.DTOs
{
    public class LeaderboardEntryDto
    {
        // Kullanıcının sıralamadaki derecesi.
        public int Rank { get; set; }

        // Kullanıcıyı dışarıya tanıtan güvenli ve anonim kimlik.
        public string PublicId { get; set; } = string.Empty;

        // Kayıtlı kullanıcının kullanıcı adı.
        public string Username { get; set; } = string.Empty;

        // Dört ondalık basamak hassasiyetindeki toplam puan.
        public decimal TotalScore { get; set; }

        // Çözülen bulmaca sayısı.
        public int SolvedCount { get; set; }
    }
}
