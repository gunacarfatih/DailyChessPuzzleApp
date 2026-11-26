
namespace DailyChessPuzzleApi.Settings
{
    public class PublicIdSettings
    {
        // PublicId üretiminde kullanılacak gizli anahtar.
        // JWT Secret'tan tamamen bağımsızdır.
        public string Secret { get; set; } = string.Empty;
    }
}
