
using DailyChessPuzzleApi.Data;
using DailyChessPuzzleApi.DTOs.Auth;
using DailyChessPuzzleApi.Models;
using DailyChessPuzzleApi.Services.Interfaces;
using DailyChessPuzzleApi.Settings;
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

        public AuthService(
            ApplicationDbContext context,
            IPasswordHasher<User> passwordHasher,
            IOptions<JwtSettings> jwtOptions)
        {
            _context = context;
            _passwordHasher = passwordHasher;
            _jwtSettings = jwtOptions.Value;
        }

        public async Task<AuthResponse> RegisterAsync(
            RegisterRequest request,
            CancellationToken cancellationToken)
        {
            string username = request.Username.Trim();
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

            if (user == null)
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
                Email = user.Email,
                CountryCode = user.CountryCode,
                AccessToken = new JwtSecurityTokenHandler()
                    .WriteToken(token),
                ExpiresAtUtc = expiresAtUtc
            };
        }
    }
}
