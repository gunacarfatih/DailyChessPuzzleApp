namespace DailyChessPuzzleApi.Services.Interfaces;

public interface IEmailSender
{
    void EnsureConfigured();

    Task SendVerificationCodeAsync(
        string email,
        string code,
        CancellationToken cancellationToken);
}
