using DailyChessPuzzleApi.Exceptions;
using DailyChessPuzzleApi.Services.Interfaces;
using Google.Apis.Auth;

namespace DailyChessPuzzleApi.Services
{
    public sealed class GoogleTokenValidator
        : IGoogleTokenValidator
    {
        private readonly IConfiguration _configuration;

        public GoogleTokenValidator(
            IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public async Task<GoogleJsonWebSignature.Payload> ValidateAsync(
            string idToken,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var clientId =
                _configuration["GoogleAuth:WebClientId"];

            if (string.IsNullOrWhiteSpace(clientId))
            {
                throw new InvalidOperationException(
                    "GOOGLE_CLIENT_ID_NOT_CONFIGURED");
            }

            GoogleJsonWebSignature.Payload payload;

            try
            {
                payload = await GoogleJsonWebSignature.ValidateAsync(
                    idToken,
                    new GoogleJsonWebSignature.ValidationSettings
                    {
                        Audience = new[] { clientId }
                    });
            }
            catch (InvalidJwtException)
            {
                throw new AppException(
                    401,
                    "INVALID_GOOGLE_TOKEN");
            }

            cancellationToken.ThrowIfCancellationRequested();

            return payload;
        }
    }
}