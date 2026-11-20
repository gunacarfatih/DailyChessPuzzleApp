namespace DailyChessPuzzleApi.Services.Interfaces
{
    public interface IPublicIdService
    {
        // Veritabanındaki kullanıcı ID'sinden
        // dışarıya açık, güvenli PublicId üretir.
        string Generate(int userId);
    }
}