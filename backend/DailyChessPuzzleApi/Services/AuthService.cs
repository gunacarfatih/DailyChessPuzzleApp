using DailyChessPuzzleApi.Data;
using DailyChessPuzzleApi.DTOs.Auth;
using DailyChessPuzzleApi.Models;
using DailyChessPuzzleApi.Services.Interfaces;
using DailyChessPuzzleApi.Settings;
using DailyChessPuzzleApi.Exceptions;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace DailyChessPuzzleApi.Services
{
    public class AuthService : IAuthService
    {
        private readonly ApplicationDbContext _context;
        private readonly IPasswordHasher<User> _passwordHasher;
        private readonly JwtSettings _jwtSettings;
        private readonly IConfiguration _configuration;
        private readonly IEmailSender _emailSender;

        public AuthService(
            ApplicationDbContext context,
            IPasswordHasher<User> passwordHasher,
            IOptions<JwtSettings> jwtOptions,
            IConfiguration configuration,
            IEmailSender emailSender)
        {
            _context = context;
            _passwordHasher = passwordHasher;
            _jwtSettings = jwtOptions.Value;
            _configuration = configuration;
            _emailSender = emailSender;
        }

        public async Task<RegistrationResponse> RegisterAsync(
            RegisterRequest request,
            CancellationToken cancellationToken)
        {
            _emailSender.EnsureConfigured();
            string username = request.Username.Trim();
            string firstName = request.FirstName.Trim();
            string lastName = request.LastName.Trim();
            string email = request.Email.Trim().ToLowerInvariant();

            bool usernameExists = await _context.Users
                .AnyAsync(
                    x => x.Username.ToLower() == username.ToLower(),
                    cancellationToken);

            if (usernameExists)
                throw new AppException(409, "USERNAME_ALREADY_EXISTS");

            bool emailExists = await _context.Users
                .AnyAsync(
                    x => x.Email == email,
                    cancellationToken);

            if (emailExists)
                throw new AppException(409, "EMAIL_ALREADY_EXISTS");

            var verificationCode = GenerateEmailVerificationCode();
            var user = new User
            {
                Username = username,
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                CountryCode = request.CountryCode?
                    .Trim()
                    .ToUpperInvariant(),
                EmailVerified = false,
                EmailVerificationCodeHash = HashEmailVerificationCode(email, verificationCode),
                EmailVerificationCodeExpiresAtUtc = DateTime.UtcNow.AddMinutes(15),
                CreatedAtUtc = DateTime.UtcNow
            };

            user.PasswordHash = _passwordHasher.HashPassword(
                user,
                request.Password);

            _context.Users.Add(user);

            await _context.SaveChangesAsync(cancellationToken);

            try
            {
                await _emailSender.SendVerificationCodeAsync(
                    email,
                    verificationCode,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                throw new AppException(503, "EMAIL_DELIVERY_FAILED");
            }

            return new RegistrationResponse { Email = email };
        }

        public async Task VerifyEmailAsync(
            EmailVerificationRequest request,
            CancellationToken cancellationToken)
        {
            var email = request.Email.Trim().ToLowerInvariant();
            var user = await _context.Users.FirstOrDefaultAsync(
                x => x.Email == email,
                cancellationToken);

            if (user == null || user.EmailVerified ||
                user.EmailVerificationCodeHash == null ||
                !user.EmailVerificationCodeExpiresAtUtc.HasValue ||
                user.EmailVerificationCodeExpiresAtUtc.Value <= DateTime.UtcNow ||
                !CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(user.EmailVerificationCodeHash),
                    Convert.FromHexString(HashEmailVerificationCode(email, request.Code))))
            {
                throw new AppException(400, "EMAIL_VERIFICATION_CODE_INVALID");
            }

            user.EmailVerified = true;
            user.EmailVerificationCodeHash = null;
            user.EmailVerificationCodeExpiresAtUtc = null;
            user.UpdatedAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }

        public async Task ResendVerificationAsync(
            ResendVerificationRequest request,
            CancellationToken cancellationToken)
        {
            _emailSender.EnsureConfigured();

            var email = request.Email.Trim().ToLowerInvariant();
            var user = await _context.Users.FirstOrDefaultAsync(
                x => x.Email == email,
                cancellationToken);

            if (user == null || user.EmailVerified)
                return;

            var code = GenerateEmailVerificationCode();
            user.EmailVerificationCodeHash = HashEmailVerificationCode(email, code);
            user.EmailVerificationCodeExpiresAtUtc = DateTime.UtcNow.AddMinutes(15);
            user.UpdatedAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
            try
            {
                await _emailSender.SendVerificationCodeAsync(email, code, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                throw new AppException(503, "EMAIL_DELIVERY_FAILED");
            }
        }

        public async Task<AuthResponse> LoginAsync(
            LoginRequest request,
            CancellationToken cancellationToken)
        {
            string email = request.Email.Trim().ToLowerInvariant();

            var user = await _context.Users
                .FirstOrDefaultAsync(
                    x => x.Email == email,
                    cancellationToken);

            if (user == null || string.IsNullOrEmpty(user.PasswordHash))
                throw new AppException(401, "INVALID_CREDENTIALS");

            var verificationResult = _passwordHasher.VerifyHashedPassword(
                user,
                user.PasswordHash,
                request.Password);

            if (verificationResult == PasswordVerificationResult.Failed)
                throw new AppException(401, "INVALID_CREDENTIALS");

            if (!user.EmailVerified)
                throw new AppException(403, "EMAIL_NOT_VERIFIED");

            if (verificationResult ==
                PasswordVerificationResult.SuccessRehashNeeded)
            {
                user.PasswordHash = _passwordHasher.HashPassword(
                    user,
                    request.Password);

                user.UpdatedAtUtc = DateTime.UtcNow;

                await _context.SaveChangesAsync(cancellationToken);
            }

            return await CreateAuthResponseAsync(user, cancellationToken);
        }

        public async Task<AuthResponse> RefreshAsync(
    RefreshTokenRequest request,
    CancellationToken cancellationToken)
        {
            await using var transaction =
                await _context.Database.BeginTransactionAsync(
                    cancellationToken);

            var tokenHash =
                HashRefreshToken(request.RefreshToken);

            var refreshToken = await _context.RefreshTokens
                .FromSqlInterpolated($"""
            SELECT *
            FROM "RefreshTokens"
            WHERE "TokenHash" = {tokenHash}
            FOR UPDATE
            """)
                .SingleOrDefaultAsync(cancellationToken);

            var now = DateTime.UtcNow;

            if (refreshToken == null ||
                refreshToken.ExpiresAtUtc <= now)
            {
                throw new AppException(
                    401,
                    "INVALID_REFRESH_TOKEN");
            }

            // Yerine başka token oluşturulmuş, iptal edilmiş bir
            // token tekrar sunulduysa token hırsızlığı ihtimali vardır.
            if (refreshToken.RevokedAtUtc.HasValue &&
                !string.IsNullOrWhiteSpace(
                    refreshToken.ReplacedByTokenHash))
            {
                var user = await _context.Users
                    .FirstOrDefaultAsync(
                        x => x.Id == refreshToken.UserId,
                        cancellationToken);

                if (user != null)
                {
                    // Bu ailede hâlâ aktif olan yeni token'ları iptal et.
                    await _context.RefreshTokens
                        .Where(token =>
                            token.FamilyId == refreshToken.FamilyId &&
                            token.RevokedAtUtc == null)
                        .ExecuteUpdateAsync(
                            setters => setters.SetProperty(
                                token => token.RevokedAtUtc,
                                now),
                            cancellationToken);

                    // Bu aileden üretilmiş mevcut access token'ı da reddet.
                    // Diğer cihazlar geçerli refresh token ile tekrar
                    // yenileme yaparak yeni sürüme geçebilir.
                    user.TokenVersion++;
                    user.UpdatedAtUtc = now;

                    await _context.SaveChangesAsync(
                        cancellationToken);
                }

                await transaction.CommitAsync(
                    cancellationToken);

                throw new AppException(
                    401,
                    "REFRESH_TOKEN_REUSE_DETECTED");
            }

            // Logout veya şifre değişimi nedeniyle iptal edilmiş,
            // fakat yerine başka token oluşturulmamış token.
            if (refreshToken.RevokedAtUtc.HasValue)
            {
                throw new AppException(
                    401,
                    "INVALID_REFRESH_TOKEN");
            }

            var currentUser = await _context.Users
                .FirstOrDefaultAsync(
                    x => x.Id == refreshToken.UserId,
                    cancellationToken);

            if (currentUser == null)
            {
                throw new AppException(
                    401,
                    "INVALID_REFRESH_TOKEN");
            }

            // Kullanılmış refresh token'ı iptal et.
            refreshToken.RevokedAtUtc = now;

            // Aynı token ailesinde yeni access ve refresh token oluştur.
            var response = await CreateAuthResponseAsync(
                currentUser,
                cancellationToken,
                refreshToken.FamilyId);

            refreshToken.ReplacedByTokenHash =
                HashRefreshToken(response.RefreshToken!);

            await _context.SaveChangesAsync(
                cancellationToken);

            await transaction.CommitAsync(
                cancellationToken);

            return response;
        }

        public async Task LogoutAsync(
            RefreshTokenRequest request,
            CancellationToken cancellationToken)
        {
            var tokenHash = HashRefreshToken(
                request.RefreshToken);

            var now = DateTime.UtcNow;

            await _context.RefreshTokens
                .Where(token =>
                    token.TokenHash == tokenHash &&
                    token.RevokedAtUtc == null)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        token => token.RevokedAtUtc,
                        now),
                    cancellationToken);
        }

        public async Task<AuthResponse> GoogleLoginAsync(
            GoogleLoginRequest request,
            CancellationToken cancellationToken)
        {
            string? googleClientId =
                _configuration["GoogleAuth:WebClientId"];

            if (string.IsNullOrWhiteSpace(googleClientId))
            {
                throw new InvalidOperationException(
                    "GOOGLE_CLIENT_ID_NOT_CONFIGURED");
            }

            GoogleJsonWebSignature.Payload payload;

            try
            {
                payload = await GoogleJsonWebSignature.ValidateAsync(
                    request.IdToken,
                    new GoogleJsonWebSignature.ValidationSettings
                    {
                        Audience = new[] { googleClientId }
                    });
            }
            catch (InvalidJwtException)
            {
                throw new AppException(401, "INVALID_GOOGLE_TOKEN");
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(payload.Subject) ||
                string.IsNullOrWhiteSpace(payload.Email) ||
                !payload.EmailVerified)
            {
                throw new AppException(401, "GOOGLE_EMAIL_NOT_VERIFIED");
            }

            string googleId = payload.Subject;
            string email = payload.Email.Trim().ToLowerInvariant();

            var user = await _context.Users
                .FirstOrDefaultAsync(
                    x => x.GoogleId == googleId,
                    cancellationToken);

            if (user != null)
                return await CreateAuthResponseAsync(user, cancellationToken);

            var existingUser = await _context.Users
                .FirstOrDefaultAsync(
                    x => x.Email == email,
                    cancellationToken);

            if (existingUser != null)
            {
                if (!existingUser.EmailVerified)
                {
                    await using var transaction =
                        await _context.Database.BeginTransactionAsync(
                            cancellationToken);

                    try
                    {
                        var now = DateTime.UtcNow;

                        existingUser.GoogleId = googleId;
                        existingUser.EmailVerified = true;

                        // Doğrulanmamış hesap oluşturulurken belirlenen şifreye
                        // artık güvenilemez. Google doğrulamasından sonra silinir.
                        existingUser.PasswordHash = null;

                        existingUser.EmailVerificationCodeHash = null;
                        existingUser.EmailVerificationCodeExpiresAtUtc = null;
                        existingUser.UpdatedAtUtc = now;
                        existingUser.TokenVersion++;

                        // Bu kullanıcı için daha önce oluşmuş olabilecek bütün
                        // aktif refresh token'ları geçersiz kıl.
                        await _context.RefreshTokens
                            .Where(token =>
                                token.UserId == existingUser.Id &&
                                token.RevokedAtUtc == null)
                            .ExecuteUpdateAsync(
                                setters => setters.SetProperty(
                                    token => token.RevokedAtUtc,
                                    now),
                                cancellationToken);

                        await _context.SaveChangesAsync(cancellationToken);

                        // Google ile giriş yapan gerçek kullanıcı için yeni
                        // access token ve refresh token oluşturulur.
                        var response = await CreateAuthResponseAsync(
                            existingUser,
                            cancellationToken);

                        await transaction.CommitAsync(cancellationToken);

                        return response;
                    }
                    catch
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        throw;
                    }
                }

                throw new AppException(
                    409,
                    "GOOGLE_ACCOUNT_LINK_REQUIRED");
            }

            string username = await GenerateGoogleUsernameAsync(
                email,
                cancellationToken);

            user = new User
            {
                Username = username,
                FirstName = payload.GivenName?.Trim() ?? string.Empty,
                LastName = payload.FamilyName?.Trim() ?? string.Empty,
                Email = email,
                GoogleId = googleId,
                EmailVerified = true,
                PasswordHash = null,
                CountryCode = null,
                CreatedAtUtc = DateTime.UtcNow
            };

            _context.Users.Add(user);

            await _context.SaveChangesAsync(cancellationToken);

            return await CreateAuthResponseAsync(user, cancellationToken);
        }

        public async Task LinkGoogleAccountAsync(
            int userId,
            LinkGoogleAccountRequest request,
            CancellationToken cancellationToken)
        {
            var user = await _context.Users.FirstOrDefaultAsync(
                x => x.Id == userId,
                cancellationToken);
            if (user == null)
                throw new AppException(404, "USER_NOT_FOUND");

            if (string.IsNullOrWhiteSpace(user.PasswordHash))
                throw new AppException(409, "PASSWORD_NOT_SET");

            if (_passwordHasher.VerifyHashedPassword(
                    user,
                    user.PasswordHash,
                    request.CurrentPassword) == PasswordVerificationResult.Failed)
            {
                throw new AppException(401, "INVALID_CREDENTIALS");
            }

            var clientId = _configuration["GoogleAuth:WebClientId"];
            if (string.IsNullOrWhiteSpace(clientId))
                throw new InvalidOperationException("Google client ID is not configured.");

            GoogleJsonWebSignature.Payload payload;
            try
            {
                payload = await GoogleJsonWebSignature.ValidateAsync(
                    request.IdToken,
                    new GoogleJsonWebSignature.ValidationSettings
                    {
                        Audience = new[] { clientId }
                    });
            }
            catch (InvalidJwtException)
            {
                throw new AppException(401, "INVALID_GOOGLE_TOKEN");
            }

            if (!payload.EmailVerified ||
                !string.Equals(payload.Email?.Trim(), user.Email, StringComparison.OrdinalIgnoreCase))
            {
                throw new AppException(403, "GOOGLE_EMAIL_MISMATCH");
            }

            var alreadyLinked = await _context.Users.AnyAsync(
                x => x.Id != userId && x.GoogleId == payload.Subject,
                cancellationToken);
            if (alreadyLinked)
                throw new AppException(409, "GOOGLE_ACCOUNT_ALREADY_LINKED");

            user.GoogleId = payload.Subject;
            user.EmailVerified = true;
            user.UpdatedAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }

        public async Task SetPasswordAsync(
            int userId,
            SetPasswordRequest request,
            CancellationToken cancellationToken)
        {
            var user = await _context.Users
                .FirstOrDefaultAsync(
                    x => x.Id == userId,
                    cancellationToken);

            if (user == null)
                throw new KeyNotFoundException("USER_NOT_FOUND");

            if (!string.IsNullOrWhiteSpace(user.PasswordHash))
            {
                throw new AppException(409, "PASSWORD_ALREADY_SET");
            }

            user.PasswordHash = _passwordHasher.HashPassword(
                user,
                request.NewPassword);

            user.UpdatedAtUtc = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);
        }

        public async Task ChangePasswordAsync(
            int userId,
            ChangePasswordRequest request,
            CancellationToken cancellationToken)
        {
            var user = await _context.Users
                .FirstOrDefaultAsync(
                    x => x.Id == userId,
                    cancellationToken);

            if (user == null)
                throw new KeyNotFoundException("USER_NOT_FOUND");

            if (string.IsNullOrWhiteSpace(user.PasswordHash))
            {
                throw new AppException(
                    409,
                    "PASSWORD_NOT_SET");
            }

            var verificationResult =
                _passwordHasher.VerifyHashedPassword(
                    user,
                    user.PasswordHash,
                    request.CurrentPassword);

            if (verificationResult ==
                PasswordVerificationResult.Failed)
            {
                throw new UnauthorizedAccessException(
                    "INVALID_CURRENT_PASSWORD");
            }

            await using var transaction =
                await _context.Database.BeginTransactionAsync(
                    cancellationToken);

            try
            {
                var now = DateTime.UtcNow;

                user.PasswordHash = _passwordHasher.HashPassword(
                    user,
                    request.NewPassword);

                user.UpdatedAtUtc = now;
                user.TokenVersion++;

                await _context.RefreshTokens
                    .Where(token =>
                        token.UserId == userId &&
                        token.RevokedAtUtc == null)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(
                            token => token.RevokedAtUtc,
                            now),
                        cancellationToken);

                await _context.SaveChangesAsync(cancellationToken);

                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }

        private async Task<string> GenerateGoogleUsernameAsync(
            string email,
            CancellationToken cancellationToken)
        {
            string emailPrefix = email.Split('@')[0];

            string baseUsername = new string(
                emailPrefix
                    .ToLowerInvariant()
                    .Select(character =>
                        char.IsLetterOrDigit(character) ||
                        character == '_'
                            ? character
                            : '_')
                    .ToArray());

            if (baseUsername.Length < 3)
                baseUsername = "user";

            if (baseUsername.Length > 45)
                baseUsername = baseUsername[..45];

            string username = baseUsername;
            int suffix = 1;

            while (await _context.Users.AnyAsync(
                x => x.Username.ToLower() == username.ToLower(),
                cancellationToken))
            {
                username = $"{baseUsername}{suffix}";
                suffix++;
            }

            return username;
        }

        private async Task<AuthResponse> CreateAuthResponseAsync(
            User user,
            CancellationToken cancellationToken,
            Guid? refreshTokenFamilyId = null)
        {
            DateTime expiresAtUtc = DateTime.UtcNow.AddMinutes(
                _jwtSettings.ExpirationMinutes);

            var claims = new List<Claim>
            {
                new Claim(
                    JwtRegisteredClaimNames.Sub,
                    user.Id.ToString()),

                new Claim(
                    ClaimTypes.NameIdentifier,
                    user.Id.ToString()),

                new Claim(
                    ClaimTypes.Name,
                    user.Username),

                new Claim(
                    JwtRegisteredClaimNames.Email,
                    user.Email),

                new Claim(
                    "token_version",
                    user.TokenVersion.ToString())
            };

            var key = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(_jwtSettings.Secret));

            var credentials = new SigningCredentials(
                key,
                SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _jwtSettings.Issuer,
                audience: _jwtSettings.Audience,
                claims: claims,
                expires: expiresAtUtc,
                signingCredentials: credentials);

            var refreshToken = Convert.ToBase64String(
                    RandomNumberGenerator.GetBytes(48))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
            var refreshTokenExpiresAtUtc = DateTime.UtcNow.AddDays(30);
            _context.RefreshTokens.Add(new RefreshToken
            {
                UserId = user.Id,
                TokenHash = HashRefreshToken(refreshToken),
                FamilyId = refreshTokenFamilyId ?? Guid.NewGuid(),
                CreatedAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = refreshTokenExpiresAtUtc
            });
            await _context.SaveChangesAsync(cancellationToken);

            return new AuthResponse
            {
                UserId = user.Id,
                Username = user.Username,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                CountryCode = user.CountryCode,
                HasPassword = !string.IsNullOrWhiteSpace(
                    user.PasswordHash),
                AccessToken = new JwtSecurityTokenHandler()
                    .WriteToken(token),
                ExpiresAtUtc = expiresAtUtc,
                RefreshToken = refreshToken,
                RefreshTokenExpiresAtUtc = refreshTokenExpiresAtUtc
            };
        }

        private static string HashRefreshToken(string token) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

        private static string GenerateEmailVerificationCode() =>
            RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

        private string HashEmailVerificationCode(string email, string code)
        {
            var key = Encoding.UTF8.GetBytes(_jwtSettings.Secret);
            var value = Encoding.UTF8.GetBytes($"{email}:{code}");
            return Convert.ToHexString(HMACSHA256.HashData(key, value));
        }
    }
}
