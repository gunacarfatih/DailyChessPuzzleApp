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
using Npgsql;
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
        private readonly IGoogleTokenValidator _googleTokenValidator;

        public AuthService(
            ApplicationDbContext context,
            IPasswordHasher<User> passwordHasher,
            IOptions<JwtSettings> jwtOptions,
            IConfiguration configuration,
            IEmailSender emailSender,
            IGoogleTokenValidator? googleTokenValidator = null)
        {
            _context = context;
            _passwordHasher = passwordHasher;
            _jwtSettings = jwtOptions.Value;
            _configuration = configuration;
            _emailSender = emailSender;

            _googleTokenValidator = googleTokenValidator
                ?? new GoogleTokenValidator(configuration);
        }

        public async Task<RegistrationResponse> RegisterAsync(
            RegisterRequest request,
            CancellationToken cancellationToken)
        {
            string username = request.Username.Trim();
            string firstName = request.FirstName.Trim();
            string lastName = request.LastName.Trim();
            string email = request.Email.Trim().ToLowerInvariant();

            if (username.Length < 3 || username.Length > 50)
                throw new AppException(400, "INVALID_USERNAME");

            bool usernameExists = await UsernameExistsAsync(
                username,
                cancellationToken);

            if (usernameExists)
                throw new AppException(
                    409,
                    "USERNAME_ALREADY_EXISTS");

            bool emailExists = await _context.Users
                .AnyAsync(
                    x => x.Email == email,
                    cancellationToken);

            if (emailExists)
                throw new AppException(
                    409,
                    "EMAIL_ALREADY_EXISTS");

            var user = new User
            {
                Username = username,
                FirstName = firstName,
                LastName = lastName,
                Email = email,

                CountryCode = null,
                CountryChangedAtUtc = null,

                EmailVerified = false,
                EmailVerificationCodeHash = null,
                EmailVerificationCodeExpiresAtUtc = null,
                EmailVerificationFailedAttempts = 0,

                CreatedAtUtc = DateTime.UtcNow
            };

            user.PasswordHash = _passwordHasher.HashPassword(
                user,
                request.Password);

            _context.Users.Add(user);

            try
            {
                await _context.SaveChangesAsync(
                    cancellationToken);
            }
            catch (DbUpdateException exception)
                when (
                    exception.InnerException is PostgresException postgres &&
                    postgres.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                _context.Entry(user).State = EntityState.Detached;

                switch (postgres.ConstraintName)
                {
                    case "IX_Users_Email":
                        throw new AppException(
                            409,
                            "EMAIL_ALREADY_EXISTS");

                    case "IX_Users_Username":
                    case "IX_Users_NormalizedUsername":
                        throw new AppException(
                            409,
                            "USERNAME_ALREADY_EXISTS");

                    default:
                        throw;
                }
            }

            return new RegistrationResponse
            {
                Email = email
            };
        }


        public async Task VerifyEmailAsync(
            int userId,
            EmailVerificationRequest request,
            CancellationToken cancellationToken)
        {
            var email = request.Email.Trim().ToLowerInvariant();

            await using var transaction = await _context.Database
                .BeginTransactionAsync(cancellationToken);

            // Kullanıcıyı JWT'den gelen kimlik ile bul ve kilitle.
            var user = await GetLockedUserAsync(
                userId,
                cancellationToken);

            if (user == null)
                throw new AppException(404, "USER_NOT_FOUND");

            // Başka bir hesabın e-posta adresiyle
            // doğrulama yapılmasını engelle.
            if (!string.Equals(
                    user.Email,
                    email,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new AppException(
                    403,
                    "EMAIL_MISMATCH");
            }

            // Daha önce doğrulanmış hesap için
            // işlemi başarılı kabul et.
            if (user.EmailVerified)
            {
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            var now = DateTime.UtcNow;

            if (user.EmailVerificationCodeHash == null ||
                !user.EmailVerificationCodeExpiresAtUtc.HasValue ||
                user.EmailVerificationCodeExpiresAtUtc.Value <= now ||
                user.EmailVerificationFailedAttempts >= 5)
            {
                throw new AppException(
                    400,
                    "EMAIL_VERIFICATION_CODE_INVALID");
            }

            var codeMatches = CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(
                    user.EmailVerificationCodeHash),
                Convert.FromHexString(
                    HashEmailVerificationCode(email, request.Code)));

            if (!codeMatches)
            {
                user.EmailVerificationFailedAttempts++;

                if (user.EmailVerificationFailedAttempts >= 5)
                {
                    user.EmailVerificationCodeHash = null;
                    user.EmailVerificationCodeExpiresAtUtc = null;
                }

                user.UpdatedAtUtc = now;

                await _context.SaveChangesAsync(
                    cancellationToken);

                await transaction.CommitAsync(
                    cancellationToken);

                throw new AppException(
                    400,
                    "EMAIL_VERIFICATION_CODE_INVALID");
            }

            user.EmailVerified = true;
            user.EmailVerificationCodeHash = null;
            user.EmailVerificationCodeExpiresAtUtc = null;
            user.EmailVerificationFailedAttempts = 0;
            user.UpdatedAtUtc = now;

            await _context.SaveChangesAsync(
                cancellationToken);

            await transaction.CommitAsync(
                cancellationToken);
        }



        public async Task ResendVerificationAsync(
            int userId,
            ResendVerificationRequest request,
            CancellationToken cancellationToken)
        {
            var email = request.Email.Trim().ToLowerInvariant();

            string code;

            await using (var transaction = await _context.Database
                .BeginTransactionAsync(cancellationToken))
            {
                // VerifyEmailAsync ile aynı kullanıcı kilidini kullan.
                var user = await GetLockedUserAsync(
                    userId,
                    cancellationToken);

                if (user == null)
                    throw new AppException(404, "USER_NOT_FOUND");

                if (!string.Equals(
                        user.Email,
                        email,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new AppException(
                        403,
                        "EMAIL_MISMATCH");
                }

                // Doğrulanmış hesap için yeni kod üretme.
                if (user.EmailVerified)
                {
                    await transaction.CommitAsync(
                        cancellationToken);

                    return;
                }

                _emailSender.EnsureConfigured();

                var now = DateTime.UtcNow;

                code = GenerateEmailVerificationCode();

                user.EmailVerificationCodeHash =
                    HashEmailVerificationCode(email, code);

                user.EmailVerificationCodeExpiresAtUtc =
                    now.AddMinutes(15);

                user.EmailVerificationFailedAttempts = 0;
                user.UpdatedAtUtc = now;

                await _context.SaveChangesAsync(
                    cancellationToken);

                await transaction.CommitAsync(
                    cancellationToken);
            }

            // SMTP işlemi sırasında veritabanı kilidini tutma.
            try
            {
                await _emailSender.SendVerificationCodeAsync(
                    email,
                    code,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                throw new AppException(
                    503,
                    "EMAIL_DELIVERY_FAILED");
            }
        }


        public async Task RequestPasswordResetAsync(
            PasswordResetCodeRequest request,
            CancellationToken cancellationToken)
        {
            var email = request.Email.Trim().ToLowerInvariant();

            string code;

            await using (var transaction = await _context.Database
                .BeginTransactionAsync(cancellationToken))
            {
                var userId = await _context.Users
                    .AsNoTracking()
                    .Where(candidate => candidate.Email == email)
                    .Select(candidate => (int?)candidate.Id)
                    .SingleOrDefaultAsync(cancellationToken);

                if (!userId.HasValue)
                    return;

                var user = await GetLockedUserAsync(
                    userId.Value,
                    cancellationToken);

                if (user == null ||
                    user.Email != email ||
                    string.IsNullOrWhiteSpace(user.PasswordHash))
                {
                    return;
                }

                _emailSender.EnsureConfigured();

                var now = DateTime.UtcNow;

                code = GenerateEmailVerificationCode();

                user.PasswordResetCodeHash =
                    HashEmailVerificationCode(email, code);

                user.PasswordResetCodeExpiresAtUtc =
                    now.AddMinutes(15);

                user.PasswordResetFailedAttempts = 0;
                user.UpdatedAtUtc = now;

                await _context.SaveChangesAsync(
                    cancellationToken);

                await transaction.CommitAsync(
                    cancellationToken);
            }

            // SMTP gönderimi transaction dışında yapılır.
            try
            {
                await _emailSender.SendPasswordResetCodeAsync(
                    email,
                    code,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                throw new AppException(
                    503,
                    "EMAIL_DELIVERY_FAILED");
            }
        }



        public async Task ResetPasswordAsync(
            ResetPasswordRequest request,
            CancellationToken cancellationToken)
        {
            var email = request.Email.Trim().ToLowerInvariant();

            await using var transaction = await _context.Database
                .BeginTransactionAsync(cancellationToken);

            var userId = await _context.Users
                .AsNoTracking()
                .Where(candidate => candidate.Email == email)
                .Select(candidate => (int?)candidate.Id)
                .SingleOrDefaultAsync(cancellationToken);

            if (!userId.HasValue)
            {
                throw new AppException(
                    400,
                    "PASSWORD_RESET_CODE_INVALID");
            }

            var user = await GetLockedUserAsync(
                userId.Value,
                cancellationToken);

            if (user == null ||
                user.Email != email ||
                string.IsNullOrWhiteSpace(user.PasswordHash))
            {
                throw new AppException(
                    400,
                    "PASSWORD_RESET_CODE_INVALID");
            }

            var now = DateTime.UtcNow;

            if (user.PasswordResetFailedAttempts >= 5 ||
                user.PasswordResetCodeHash == null ||
                !user.PasswordResetCodeExpiresAtUtc.HasValue ||
                user.PasswordResetCodeExpiresAtUtc.Value <= now)
            {
                throw new AppException(
                    400,
                    "PASSWORD_RESET_CODE_INVALID");
            }

            var codeMatches =
                CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(
                        user.PasswordResetCodeHash),
                    Convert.FromHexString(
                        HashEmailVerificationCode(
                            email,
                            request.Code)));

            if (!codeMatches)
            {
                user.PasswordResetFailedAttempts++;

                if (user.PasswordResetFailedAttempts >= 5)
                {
                    user.PasswordResetCodeHash = null;
                    user.PasswordResetCodeExpiresAtUtc = null;
                }

                user.UpdatedAtUtc = now;

                await _context.SaveChangesAsync(
                    cancellationToken);

                await transaction.CommitAsync(
                    cancellationToken);

                throw new AppException(
                    400,
                    "PASSWORD_RESET_CODE_INVALID");
            }

            // Yeni şifreyi kaydet.
            user.PasswordHash = _passwordHasher.HashPassword(
                user,
                request.NewPassword);

            // Şifre sıfırlama kodunu tüket.
            user.PasswordResetCodeHash = null;
            user.PasswordResetCodeExpiresAtUtc = null;
            user.PasswordResetFailedAttempts = 0;

            // Kullanıcı e-posta sahipliğini kanıtladı.
            user.EmailVerified = true;

            // Eski e-posta doğrulama kodlarını temizle.
            user.EmailVerificationCodeHash = null;
            user.EmailVerificationCodeExpiresAtUtc = null;
            user.EmailVerificationFailedAttempts = 0;

            // Eski access token'ları geçersiz hale getir.
            user.TokenVersion++;
            user.UpdatedAtUtc = now;

            // Tüm mevcut refresh token'ları iptal et.
            await _context.RefreshTokens
                .Where(token =>
                    token.UserId == user.Id &&
                    token.RevokedAtUtc == null)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        token => token.RevokedAtUtc,
                        now),
                    cancellationToken);

            await _context.SaveChangesAsync(
                cancellationToken);

            await transaction.CommitAsync(
                cancellationToken);
        }


        public async Task<AuthResponse> LoginAsync(
    LoginRequest request,
    CancellationToken cancellationToken)
        {
            string email = request.Email
                .Trim()
                .ToLowerInvariant();

            await using var transaction =
                await _context.Database.BeginTransactionAsync(
                    cancellationToken);

            var userId = await _context.Users
                .AsNoTracking()
                .Where(x => x.Email == email)
                .Select(x => (int?)x.Id)
                .SingleOrDefaultAsync(cancellationToken);

            if (!userId.HasValue)
                throw new AppException(
                    401,
                    "INVALID_CREDENTIALS");

            var user = await GetLockedUserAsync(
                userId.Value,
                cancellationToken);

            if (user == null ||
                user.Email != email ||
                string.IsNullOrEmpty(user.PasswordHash))
            {
                throw new AppException(
                    401,
                    "INVALID_CREDENTIALS");
            }

            var verificationResult =
                _passwordHasher.VerifyHashedPassword(
                    user,
                    user.PasswordHash,
                    request.Password);

            if (verificationResult ==
                PasswordVerificationResult.Failed)
            {
                throw new AppException(
                    401,
                    "INVALID_CREDENTIALS");
            }

            if (verificationResult ==
                PasswordVerificationResult.SuccessRehashNeeded)
            {
                user.PasswordHash =
                    _passwordHasher.HashPassword(
                        user,
                        request.Password);

                user.UpdatedAtUtc = DateTime.UtcNow;

                await _context.SaveChangesAsync(
                    cancellationToken);
            }

            var response = await CreateAuthResponseAsync(
                user,
                cancellationToken);

            await transaction.CommitAsync(
                cancellationToken);

            return response;
        }

        // All refresh, logout and password-change operations acquire the user
        // lock before touching refresh token rows.
        private async Task<User?> GetLockedUserAsync(
            int userId, CancellationToken cancellationToken)
        {
            var user = await _context.Users
                .FromSqlInterpolated($"""
                    SELECT * FROM "Users" WHERE "Id" = {userId} FOR UPDATE
                    """)
                .SingleOrDefaultAsync(cancellationToken);
            if (user != null)
                await _context.Entry(user).ReloadAsync(cancellationToken);
            return user;
        }

        private Task<int?> FindTokenOwnerAsync(
            string tokenHash, CancellationToken cancellationToken) =>
            _context.RefreshTokens.AsNoTracking()
                .Where(token => token.TokenHash == tokenHash)
                .Select(token => (int?)token.UserId)
                .SingleOrDefaultAsync(cancellationToken);

        public async Task<AuthResponse> RefreshAsync(
            RefreshTokenRequest request,
            CancellationToken cancellationToken)
        {
            await using var transaction = await _context.Database
                .BeginTransactionAsync(cancellationToken);
            var tokenHash = HashRefreshToken(request.RefreshToken);
            var userId = await FindTokenOwnerAsync(tokenHash, cancellationToken);
            if (!userId.HasValue)
                throw new AppException(401, "INVALID_REFRESH_TOKEN");

            var user = await GetLockedUserAsync(userId.Value, cancellationToken);
            if (user == null)
                throw new AppException(401, "INVALID_REFRESH_TOKEN");

            var refreshToken = await _context.RefreshTokens
                .FromSqlInterpolated($"""
                    SELECT * FROM "RefreshTokens"
                    WHERE "TokenHash" = {tokenHash} FOR UPDATE
                    """)
                .SingleOrDefaultAsync(cancellationToken);
            if (refreshToken != null)
                await _context.Entry(refreshToken).ReloadAsync(cancellationToken);

            var now = DateTime.UtcNow;
            if (refreshToken == null || refreshToken.UserId != user.Id ||
                refreshToken.ExpiresAtUtc <= now)
                throw new AppException(401, "INVALID_REFRESH_TOKEN");

            if (refreshToken.RevokedAtUtc.HasValue &&
                !string.IsNullOrWhiteSpace(refreshToken.ReplacedByTokenHash))
            {
                await _context.RefreshTokens
                    .Where(token => token.UserId == user.Id &&
                        token.FamilyId == refreshToken.FamilyId &&
                        token.RevokedAtUtc == null)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(
                        token => token.RevokedAtUtc, now), cancellationToken);
                user.TokenVersion++;
                user.UpdatedAtUtc = now;
                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                throw new AppException(401, "REFRESH_TOKEN_REUSE_DETECTED");
            }

            if (refreshToken.RevokedAtUtc.HasValue)
                throw new AppException(401, "INVALID_REFRESH_TOKEN");

            refreshToken.RevokedAtUtc = now;
            var response = await CreateAuthResponseAsync(
                user, cancellationToken, refreshToken.FamilyId);
            refreshToken.ReplacedByTokenHash = HashRefreshToken(response.RefreshToken!);
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return response;
        }

        public async Task LogoutAsync(
            RefreshTokenRequest request,
            CancellationToken cancellationToken)
        {
            await using var transaction = await _context.Database
                .BeginTransactionAsync(cancellationToken);
            var tokenHash = HashRefreshToken(request.RefreshToken);
            var userId = await FindTokenOwnerAsync(tokenHash, cancellationToken);
            if (!userId.HasValue ||
                await GetLockedUserAsync(userId.Value, cancellationToken) == null)
                return;

            var token = await _context.RefreshTokens.AsNoTracking()
                .SingleOrDefaultAsync(x => x.TokenHash == tokenHash &&
                    x.UserId == userId.Value, cancellationToken);
            if (token == null)
                return;

            var now = DateTime.UtcNow;
            await _context.RefreshTokens
                .Where(x => x.UserId == userId.Value &&
                    x.FamilyId == token.FamilyId && x.RevokedAtUtc == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(
                    x => x.RevokedAtUtc, now), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        public async Task<AuthResponse> GoogleLoginAsync(
    GoogleLoginRequest request,
    CancellationToken cancellationToken)
        {
            var payload = await _googleTokenValidator.ValidateAsync(
                request.IdToken,
                cancellationToken);

            if (string.IsNullOrWhiteSpace(payload.Subject) ||
                string.IsNullOrWhiteSpace(payload.Email) ||
                !payload.EmailVerified)
            {
                throw new AppException(
                    401,
                    "GOOGLE_EMAIL_NOT_VERIFIED");
            }

            const int maxAttempts = 5;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    return await ExecuteGoogleLoginAsync(
                        payload,
                        cancellationToken);
                }
                catch (DbUpdateException exception)
                    when (IsGoogleRegistrationConflict(exception))
                {
                    // ExecuteGoogleLoginAsync içerisindeki transaction
                    // dispose edildi ve tamamlanmamış işlem geri alındı.

                    _context.ChangeTracker.Clear();

                    if (attempt == maxAttempts)
                    {
                        throw new AppException(
                            503,
                            "GOOGLE_LOGIN_TEMPORARILY_UNAVAILABLE");
                    }

                    await Task.Delay(
                        Random.Shared.Next(20, 80) * attempt,
                        cancellationToken);
                }
            }

            throw new AppException(
                503,
                "GOOGLE_LOGIN_TEMPORARILY_UNAVAILABLE");
        }


        private async Task<AuthResponse> ExecuteGoogleLoginAsync(
            GoogleJsonWebSignature.Payload payload,
            CancellationToken cancellationToken)
        {
            string googleId = payload.Subject;
            string email = payload.Email.Trim().ToLowerInvariant();

            await using var transaction =
                await _context.Database.BeginTransactionAsync(
                    cancellationToken);

            // Öncelikle Google kimliği üzerinden kullanıcıyı ara.
            var userId = await _context.Users
                .AsNoTracking()
                .Where(x => x.GoogleId == googleId)
                .Select(x => (int?)x.Id)
                .SingleOrDefaultAsync(cancellationToken);

            User user;

            if (userId.HasValue)
            {
                user = await GetLockedUserAsync(
                    userId.Value,
                    cancellationToken)
                    ?? throw new AppException(
                        409,
                        "GOOGLE_ACCOUNT_LINK_REQUIRED");

                // Kullanıcı kilitlendikten sonra kimliği tekrar doğrula.
                if (user.GoogleId != googleId)
                {
                    throw new AppException(
                        409,
                        "GOOGLE_ACCOUNT_LINK_REQUIRED");
                }
            }
            else
            {
                // Google kimliği bulunamadıysa e-posta adresini kontrol et.
                var existingUserId = await _context.Users
                    .AsNoTracking()
                    .Where(x => x.Email == email)
                    .Select(x => (int?)x.Id)
                    .SingleOrDefaultAsync(cancellationToken);

                if (existingUserId.HasValue)
                {
                    var existingUser = await GetLockedUserAsync(
                        existingUserId.Value,
                        cancellationToken);

                    // Eş zamanlı hesap bağlama işlemi tamamlanmış olabilir.
                    if (existingUser != null &&
                        existingUser.GoogleId == googleId)
                    {
                        user = existingUser;
                    }
                    else
                    {
                        // E-posta eşleşmesi tek başına mevcut hesabı
                        // devralmak için yeterli değildir.
                        throw new AppException(
                            409,
                            "GOOGLE_ACCOUNT_LINK_REQUIRED");
                    }
                }
                else
                {
                    string username = await GenerateGoogleUsernameAsync(
                        email,
                        cancellationToken);

                    user = new User
                    {
                        Username = username,

                        FirstName =
                            payload.GivenName?.Trim() ?? string.Empty,

                        LastName =
                            payload.FamilyName?.Trim() ?? string.Empty,

                        Email = email,
                        GoogleId = googleId,

                        EmailVerified = true,
                        EmailVerificationCodeHash = null,
                        EmailVerificationCodeExpiresAtUtc = null,
                        EmailVerificationFailedAttempts = 0,

                        PasswordHash = null,

                        CountryCode = null,
                        CountryChangedAtUtc = null,

                        CreatedAtUtc = DateTime.UtcNow
                    };

                    _context.Users.Add(user);

                    await _context.SaveChangesAsync(
                        cancellationToken);
                }
            }

            var response = await CreateAuthResponseAsync(
                user,
                cancellationToken);

            await transaction.CommitAsync(
                cancellationToken);

            return response;
        }


        private static bool IsGoogleRegistrationConflict(
            DbUpdateException exception)
        {
            return exception.InnerException is PostgresException postgres &&
                   postgres.SqlState == PostgresErrorCodes.UniqueViolation &&
                   postgres.ConstraintName is
                       "IX_Users_Email" or
                       "IX_Users_Username" or
                       "IX_Users_NormalizedUsername" or
                       "IX_Users_GoogleId";
        }

        public async Task LinkGoogleAccountAsync(
            int userId,
            LinkGoogleAccountRequest request,
            CancellationToken cancellationToken)
        {
            var payload = await _googleTokenValidator.ValidateAsync(
                request.IdToken,
                cancellationToken);

            await using var transaction =
                await _context.Database.BeginTransactionAsync(
                    cancellationToken);

            var user = await GetLockedUserAsync(
                userId,
                cancellationToken);

            if (user == null)
            {
                throw new AppException(
                    404,
                    "USER_NOT_FOUND");
            }

            if (string.IsNullOrWhiteSpace(user.PasswordHash))
            {
                throw new AppException(
                    409,
                    "PASSWORD_NOT_SET");
            }

            // Mevcut hesap şifresini doğrula.
            if (_passwordHasher.VerifyHashedPassword(
                    user,
                    user.PasswordHash,
                    request.CurrentPassword)
                == PasswordVerificationResult.Failed)
            {
                throw new AppException(
                    401,
                    "INVALID_CREDENTIALS");
            }

            // Google hesabının doğrulanmış olduğunu ve
            // mevcut hesapla aynı e-posta adresine sahip
            // olduğunu kontrol et.
            if (string.IsNullOrWhiteSpace(payload.Subject) ||
                !payload.EmailVerified ||
                !string.Equals(
                    payload.Email?.Trim(),
                    user.Email,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new AppException(
                    403,
                    "GOOGLE_EMAIL_MISMATCH");
            }

            // Farklı bir Google hesabı zaten bağlıysa reddet.
            if (!string.IsNullOrWhiteSpace(user.GoogleId) &&
                user.GoogleId != payload.Subject)
            {
                throw new AppException(
                    409,
                    "GOOGLE_ACCOUNT_ALREADY_LINKED");
            }

            // Google kimliğinin başka kullanıcıya ait
            // olmadığını kontrol et.
            var alreadyLinked = await _context.Users.AnyAsync(
                x => x.Id != userId &&
                     x.GoogleId == payload.Subject,
                cancellationToken);

            if (alreadyLinked)
            {
                throw new AppException(
                    409,
                    "GOOGLE_ACCOUNT_ALREADY_LINKED");
            }

            // Aynı Google hesabı zaten bağlıysa
            // işlemi idempotent olarak başarılı kabul et.
            if (user.GoogleId == payload.Subject &&
                user.EmailVerified)
            {
                await transaction.CommitAsync(
                    cancellationToken);

                return;
            }

            user.GoogleId = payload.Subject;
            user.EmailVerified = true;

            // Artık e-posta doğrulama koduna ihtiyaç yok.
            user.EmailVerificationCodeHash = null;
            user.EmailVerificationCodeExpiresAtUtc = null;
            user.EmailVerificationFailedAttempts = 0;

            // Daha önce üretilmiş şifre sıfırlama
            // kodlarını geçersiz hale getir.
            user.PasswordResetCodeHash = null;
            user.PasswordResetCodeExpiresAtUtc = null;
            user.PasswordResetFailedAttempts = 0;

            user.UpdatedAtUtc = DateTime.UtcNow;

            try
            {
                await _context.SaveChangesAsync(
                    cancellationToken);

                await transaction.CommitAsync(
                    cancellationToken);
            }
            catch (DbUpdateException exception)
                when (
                    exception.InnerException is PostgresException postgres &&
                    postgres.SqlState == PostgresErrorCodes.UniqueViolation &&
                    postgres.ConstraintName == "IX_Users_GoogleId")
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                _context.ChangeTracker.Clear();

                throw new AppException(
                    409,
                    "GOOGLE_ACCOUNT_ALREADY_LINKED");
            }
        }


        public async Task SetPasswordAsync(
    int userId,
    SetPasswordRequest request,
    CancellationToken cancellationToken)
        {
            await using var transaction =
                await _context.Database.BeginTransactionAsync(
                    cancellationToken);

            var user = await GetLockedUserAsync(
                userId,
                cancellationToken);

            if (user == null)
                throw new KeyNotFoundException(
                    "USER_NOT_FOUND");

            if (!string.IsNullOrWhiteSpace(
                user.PasswordHash))
            {
                throw new AppException(
                    409,
                    "PASSWORD_ALREADY_SET");
            }

            user.PasswordHash =
                _passwordHasher.HashPassword(
                    user,
                    request.NewPassword);

            user.UpdatedAtUtc = DateTime.UtcNow;

            await _context.SaveChangesAsync(
                cancellationToken);

            await transaction.CommitAsync(
                cancellationToken);
        }

        public async Task ChangePasswordAsync(
            int userId,
            ChangePasswordRequest request,
            CancellationToken cancellationToken)
        {
            await using var transaction =
                await _context.Database.BeginTransactionAsync(
                    cancellationToken);

            var user = await GetLockedUserAsync(userId, cancellationToken);

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

        public async Task DeleteAccountAsync(
            int userId,
            DeleteAccountRequest request,
            CancellationToken cancellationToken)
        {
            await using var transaction = await _context.Database
                .BeginTransactionAsync(cancellationToken);
            var user = await GetLockedUserAsync(userId, cancellationToken);
            if (user == null)
                return;

            if (!string.IsNullOrWhiteSpace(user.PasswordHash))
            {
                if (string.IsNullOrEmpty(request.CurrentPassword))
                    throw new AppException(400, "CURRENT_PASSWORD_REQUIRED");
                if (_passwordHasher.VerifyHashedPassword(user, user.PasswordHash,
                    request.CurrentPassword) == PasswordVerificationResult.Failed)
                    throw new AppException(403, "INVALID_CURRENT_PASSWORD");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(request.GoogleIdToken) ||
                    string.IsNullOrWhiteSpace(user.GoogleId))
                    throw new AppException(400, "GOOGLE_REAUTH_REQUIRED");
                GoogleJsonWebSignature.Payload proof;
                try { proof = await _googleTokenValidator.ValidateAsync(request.GoogleIdToken, cancellationToken); }
                catch (AppException) { throw new AppException(403, "INVALID_GOOGLE_TOKEN"); }
                if (!string.Equals(proof.Subject, user.GoogleId, StringComparison.Ordinal))
                    throw new AppException(403, "GOOGLE_ACCOUNT_MISMATCH");
                // auth_time must be signed by Google; iat alone is not reauthentication.
                var jwt = new JwtSecurityTokenHandler().ReadJwtToken(request.GoogleIdToken);
                var authTime = jwt.Claims.FirstOrDefault(c => c.Type == "auth_time")?.Value;
                var nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if (!long.TryParse(authTime, out var seconds) ||
                    seconds < nowSeconds - 120 || seconds > nowSeconds + 30)
                    throw new AppException(403, "GOOGLE_REAUTH_REQUIRED");
            }

            var attemptIds = await _context.PuzzleAttempts
                .Where(attempt => attempt.UserId == userId)
                .Select(attempt => attempt.Id)
                .ToListAsync(cancellationToken);

            var clientId = $"user:{userId}";
            var targets = await _context.LeaderboardScores
                .Where(x => x.UserId == userId || x.ClientId == clientId)
                .Select(x => new { x.Period, x.PeriodKey, x.CountryCode })
                .ToListAsync(cancellationToken);
            var pendingTargets = await _context.LeaderboardCacheOutboxDeliveries
                .Where(x => x.TargetUserId == userId || x.TargetClientId == clientId)
                .Select(x => new { x.Period, x.PeriodKey, x.CountryCode })
                .ToListAsync(cancellationToken);
            targets.AddRange(pendingTargets);
            await _context.LeaderboardCacheOutboxDeliveries
                .Where(x => x.TargetUserId == userId || x.TargetClientId == clientId)
                .ExecuteDeleteAsync(cancellationToken);
            await _context.LeaderboardCacheOutboxes
                .Where(outbox => outbox.UserId == userId ||
                    (outbox.PuzzleAttemptId.HasValue &&
                     attemptIds.Contains(outbox.PuzzleAttemptId.Value)))
                .ExecuteDeleteAsync(cancellationToken);
            await _context.BonusPuzzleAssignments
                .Where(assignment => assignment.UserId == userId)
                .ExecuteDeleteAsync(cancellationToken);
            await _context.PuzzleGameSessions
                .Where(session => session.UserId == userId)
                .ExecuteDeleteAsync(cancellationToken);
            await _context.LeaderboardScores
                .Where(score => score.UserId == userId)
                .ExecuteDeleteAsync(cancellationToken);
            await _context.PuzzleAttempts
                .Where(attempt => attempt.UserId == userId)
                .ExecuteDeleteAsync(cancellationToken);

            // Archive tables are optional on installations
            // without the scoring reset.
            await _context.Database.ExecuteSqlAsync(
                $"SELECT set_config('checkrix.deletion_user_id', {userId.ToString(System.Globalization.CultureInfo.InvariantCulture)}, true)",
                cancellationToken);

            await _context.Database.ExecuteSqlAsync($"""
                DO $archive$
                BEGIN
                    IF to_regclass('scoring_archive."LeaderboardCacheOutboxDeliveries"') IS NOT NULL THEN
                        DELETE FROM scoring_archive."LeaderboardCacheOutboxDeliveries"
                        WHERE "TargetUserId" = current_setting('checkrix.deletion_user_id')::integer;
                    END IF;

                    IF to_regclass('scoring_archive."LeaderboardCacheOutboxes"') IS NOT NULL THEN
                        DELETE FROM scoring_archive."LeaderboardCacheOutboxes"
                        WHERE "UserId" = current_setting('checkrix.deletion_user_id')::integer;
                    END IF;

                    IF to_regclass('scoring_archive."BonusPuzzleAssignments"') IS NOT NULL THEN
                        DELETE FROM scoring_archive."BonusPuzzleAssignments"
                        WHERE "UserId" = current_setting('checkrix.deletion_user_id')::integer;
                    END IF;

                    IF to_regclass('scoring_archive."PuzzleGameSessions"') IS NOT NULL THEN
                        DELETE FROM scoring_archive."PuzzleGameSessions"
                        WHERE "UserId" = current_setting('checkrix.deletion_user_id')::integer;
                    END IF;

                    IF to_regclass('scoring_archive."LeaderboardScores"') IS NOT NULL THEN
                        DELETE FROM scoring_archive."LeaderboardScores"
                        WHERE "UserId" = current_setting('checkrix.deletion_user_id')::integer;
                    END IF;

                    IF to_regclass('scoring_archive."PuzzleAttempts"') IS NOT NULL THEN
                        DELETE FROM scoring_archive."PuzzleAttempts"
                        WHERE "UserId" = current_setting('checkrix.deletion_user_id')::integer;
                    END IF;
                END;
                $archive$;
                """, cancellationToken);
            var removal = new LeaderboardCacheOutbox { ClientId = clientId };
            foreach (var target in targets.Distinct())
                removal.Deliveries.Add(new LeaderboardCacheOutboxDelivery
                {
                    TargetClientId = clientId, IsRemoval = true,
                    Period = target.Period, PeriodKey = target.PeriodKey,
                    CountryCode = target.CountryCode,
                    CacheKey = $"DailyChessPuzzle_leaderboard:v3:{target.Period}:{target.PeriodKey}:{target.CountryCode ?? "global"}"
                });
            if (removal.Deliveries.Count > 0)
                _context.LeaderboardCacheOutboxes.Add(removal);
            _context.Users.Remove(user);
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        private Task<bool> UsernameExistsAsync(
            string username,
            CancellationToken cancellationToken)
        {
            // Use the same PostgreSQL lower() rule as the generated column.
            // FromSqlInterpolated parameterizes the supplied username.
            return _context.Users
                .FromSqlInterpolated($"""
                    SELECT * FROM "Users"
                    WHERE "NormalizedUsername" = lower({username})
                    """)
                .AsNoTracking()
                .AnyAsync(cancellationToken);
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

            while (await UsernameExistsAsync(username, cancellationToken))
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
                EmailVerified = user.EmailVerified,
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
