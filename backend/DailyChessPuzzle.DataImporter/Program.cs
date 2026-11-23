
using DailyChessPuzzleApi.Data;
using DailyChessPuzzle.DataImporter.Import;
using Microsoft.EntityFrameworkCore;

// PostgreSQL bağlantısını ortam değişkeninden al.
// Şifreyi kaynak kodun içerisine yazmıyoruz.
var connectionString =
    Environment.GetEnvironmentVariable(
        "ConnectionStrings__DefaultConnection");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "DefaultConnection ortam değişkeni bulunamadı!");
}

// PostgreSQL için Entity Framework Core yapılandırması.
var options = new DbContextOptionsBuilder<ApplicationDbContext>()
    .UseNpgsql(connectionString)
    .Options;

await using var context = new ApplicationDbContext(options);

// Sunucudaki sıkıştırılmış Lichess arşivi.
// Docker konteyneri içerisinde bu yolu kullanacağız.
var filePath = "/data/lichess_db_puzzle.csv.zst";

// Aktarılacak hedef bulmaca sayısı.
const int maxRecords = 25000;

Console.WriteLine("Lichess bulmaca aktarımı başlıyor...");
Console.WriteLine($"Hedef kayıt sayısı: {maxRecords}");

var importer = new LichessPuzzleImporter(context);

await importer.ImportAsync(filePath, maxRecords);

Console.WriteLine("Importer işlemi tamamlandı.");

