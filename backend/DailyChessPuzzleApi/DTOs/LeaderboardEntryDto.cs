
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

        // Toplam puan.
        public int TotalScore { get; set; }

        // Çözülen bulmaca sayısı.
        public int SolvedCount { get; set; }
    }
}
