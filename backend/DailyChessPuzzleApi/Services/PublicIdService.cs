using System.Security.Cryptography;
using System.Text;

using DailyChessPuzzleApi.Services.Interfaces;
using DailyChessPuzzleApi.Settings;

using Microsoft.Extensions.Options;

namespace DailyChessPuzzleApi.Services
{
    public class PublicIdService : IPublicIdService
    {
        private readonly byte[] _secretKey;

        public PublicIdService(IOptions<PublicIdSettings> options)
        {
            var secret = options.Value.Secret;

            if (string.IsNullOrWhiteSpace(secret))
            {
                throw new InvalidOperationException(
                    "PublicIdSettings:Secret yapılandırması bulunamadı.");
            }

            _secretKey = Encoding.UTF8.GetBytes(secret);
        }

        public string Generate(int userId)
        {
            if (userId <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(userId),
                    "User ID sıfırdan büyük olmalıdır.");
            }

            // Aynı kullanıcı için her zaman aynı girdiyi oluşturur.
            // Domain separation sayesinde başka amaçlarla kullanılan
            // HMAC değerleriyle karışması önlenir.
            var input = $"leaderboard:v1:user:{userId}";

            var inputBytes = Encoding.UTF8.GetBytes(input);

            using var hmac = new HMACSHA256(_secretKey);

            var hash = hmac.ComputeHash(inputBytes);

            // Base64Url: URL içerisinde güvenle kullanılabilecek format.
            return Convert.ToBase64String(hash)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }
    }
}