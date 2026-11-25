using Google.Apis.Auth;

namespace DailyChessPuzzleApi.Services.Interfaces
{
    public interface IGoogleTokenValidator
    {
        Task<GoogleJsonWebSignature.Payload> ValidateAsync(
            string idToken,
            CancellationToken cancellationToken);
    }
}