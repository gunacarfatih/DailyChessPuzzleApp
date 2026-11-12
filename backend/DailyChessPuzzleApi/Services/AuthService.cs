
using DailyChessPuzzleApi.Data;
using DailyChessPuzzleApi.DTOs.Auth;
using DailyChessPuzzleApi.Models;
using DailyChessPuzzleApi.Services.Interfaces;
using DailyChessPuzzleApi.Settings;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace DailyChessPuzzleApi.Services
{
    public class AuthService : IAuthService
    {
        private readonly ApplicationDbContext _context;
        private readonly IPasswordHasher<User> _passwordHasher;
        private readonly JwtSettings _jwtSettings;
        private readonly IConfiguration _configuration;

        public AuthService(
            ApplicationDbContext context,
            IPasswordHasher<User> passwordHasher,
            IOptions<JwtSettings> jwtOptions,
            IConfiguration configuration)
        {
            _context = context;
            _passwordHasher = passwordHasher;
            _jwtSettings = jwtOptions.Value;
            _configuration = configuration;
        }

        public async Task<AuthResponse> RegisterAsync(
            RegisterRequest request,
            CancellationToken cancellationToken)
        {
            string username = request.Username.Trim();
            string firstName = request.FirstName.Trim();
            string lastName = request.LastName.Trim();
            string email = request.Email.Trim().ToLowerInvariant();

            bool usernameExists = await _context.Users
                .AnyAsync(
                    x => x.Username.ToLower() == username.ToLower(),
                    cancellationToken);

            if (usernameExists)
                throw new InvalidOperationException("USERNAME_ALREADY_EXISTS");

            bool emailExists = await _context.Users
                .AnyAsync(
                    x => x.Email == email,
                    cancellationToken);

            if (emailExists)
                throw new InvalidOperationException("EMAIL_ALREADY_EXISTS");

            var user = new User
            {
                Username = username,
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                CountryCode = request.CountryCode?
                    .Trim()
                    .ToUpperInvariant(),
                CreatedAtUtc = DateTime.UtcNow
            };

            user.PasswordHash = _passwordHasher.HashPassword(
                user,
                request.Password);

            _context.Users.Add(user);

            await _context.SaveChangesAsync(cancellationToken);

            return CreateAuthResponse(user);
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
                throw new UnauthorizedAccessException(
                    "INVALID_CREDENTIALS");

            var verificationResult = _passwordHasher.VerifyHashedPassword(
                user,
                user.PasswordHash,
                request.Password);

            if (verificationResult == PasswordVerificationResult.Failed)
                throw new UnauthorizedAccessException(
                    "INVALID_CREDENTIALS");

            if (verificationResult ==
                PasswordVerificationResult.SuccessRehashNeeded)
            {
                user.PasswordHash = _passwordHasher.HashPassword(
                    user,
                    request.Password);

                user.UpdatedAtUtc = DateTime.UtcNow;

                await _context.SaveChangesAsync(cancellationToken);
            }

            return CreateAuthResponse(user);
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
                throw new UnauthorizedAccessException(
                    "INVALID_GOOGLE_TOKEN");
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(payload.Subject) ||
                string.IsNullOrWhiteSpace(payload.Email) ||
                !payload.EmailVerified)
            {
                throw new UnauthorizedAccessException(
                    "GOOGLE_EMAIL_NOT_VERIFIED");
            }

            string googleId = payload.Subject;
            string email = payload.Email.Trim().ToLowerInvariant();

            // Kullanıcı daha önce Google ile giriş yapmış mı?
            var user = await _context.Users
                .FirstOrDefaultAsync(
                    x => x.GoogleId == googleId,
                    cancellationToken);

            if (user != null)
                return CreateAuthResponse(user);

            // Aynı e-posta ile normal kayıtlı bir hesap var mı?
            // Güvenlik nedeniyle otomatik hesap birleştirmiyoruz.
            var existingUser = await _context.Users
                .FirstOrDefaultAsync(
                    x => x.Email == email,
                    cancellationToken);

            if (existingUser != null)
            {
                throw new InvalidOperationException(
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
                PasswordHash = null,
                CountryCode = null,
                CreatedAtUtc = DateTime.UtcNow
            };

            _context.Users.Add(user);

            await _context.SaveChangesAsync(cancellationToken);

            return CreateAuthResponse(user);
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

        private AuthResponse CreateAuthResponse(User user)
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
                    user.Email)
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

            return new AuthResponse
            {
                UserId = user.Id,
                Username = user.Username,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                CountryCode = user.CountryCode,
                AccessToken = new JwtSecurityTokenHandler()
                    .WriteToken(token),
                ExpiresAtUtc = expiresAtUtc
            };
        }
    }
}
