using System.Net;
using System.Net.Mail;
using DailyChessPuzzleApi.Exceptions;
using DailyChessPuzzleApi.Services.Interfaces;

namespace DailyChessPuzzleApi.Services;

public sealed class SmtpEmailSender(
    IConfiguration configuration,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(configuration["Email:Smtp:Host"]) ||
            string.IsNullOrWhiteSpace(configuration["Email:Smtp:FromEmail"]))
        {
            throw new AppException(503, "EMAIL_DELIVERY_NOT_CONFIGURED");
        }
    }

    public async Task SendVerificationCodeAsync(
        string email,
        string code,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        var host = configuration["Email:Smtp:Host"]!;
        var port = int.TryParse(configuration["Email:Smtp:Port"], out var configuredPort)
            ? configuredPort
            : 587;
        var fromEmail = configuration["Email:Smtp:FromEmail"]!;
        var fromName = configuration["Email:Smtp:FromName"] ?? "Daily Chess Puzzle";
        var username = configuration["Email:Smtp:Username"];
        var password = configuration["Email:Smtp:Password"];

        using var message = new MailMessage
        {
            From = new MailAddress(fromEmail, fromName),
            Subject = "Daily Chess Puzzle e-posta doğrulama kodu",
            Body = $"E-posta doğrulama kodunuz: {code}\n\nKod 15 dakika içinde geçerliliğini yitirir.",
            IsBodyHtml = false
        };
        message.To.Add(email);

        using var client = new SmtpClient(host, port)
        {
            EnableSsl = bool.TryParse(configuration["Email:Smtp:EnableSsl"], out var enableSsl)
                ? enableSsl
                : true,
            DeliveryMethod = SmtpDeliveryMethod.Network
        };

        if (!string.IsNullOrWhiteSpace(username))
            client.Credentials = new NetworkCredential(username, password);

        try
        {
            await client.SendMailAsync(message, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "SMTP verification email delivery failed.");
            throw;
        }
    }

    public async Task SendPasswordResetCodeAsync(
        string email,
        string code,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var host = configuration["Email:Smtp:Host"]!;
        var port = int.TryParse(configuration["Email:Smtp:Port"], out var configuredPort)
            ? configuredPort : 587;
        var fromEmail = configuration["Email:Smtp:FromEmail"]!;
        var fromName = configuration["Email:Smtp:FromName"] ?? "Daily Chess Puzzle";
        var username = configuration["Email:Smtp:Username"];
        var password = configuration["Email:Smtp:Password"];
        using var message = new MailMessage
        {
            From = new MailAddress(fromEmail, fromName),
            Subject = "Daily Chess Puzzle şifre sıfırlama kodu",
            Body = $"Şifre sıfırlama kodunuz: {code}\n\nKod 15 dakika içinde geçerliliğini yitirir. Bu isteği siz yapmadıysanız e-postayı yok sayabilirsiniz.",
            IsBodyHtml = false
        };
        message.To.Add(email);
        using var client = new SmtpClient(host, port)
        {
            EnableSsl = bool.TryParse(configuration["Email:Smtp:EnableSsl"], out var enableSsl)
                ? enableSsl : true,
            DeliveryMethod = SmtpDeliveryMethod.Network
        };
        if (!string.IsNullOrWhiteSpace(username))
            client.Credentials = new NetworkCredential(username, password);
        try
        {
            await client.SendMailAsync(message, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            logger.LogError(exception, "SMTP password reset email delivery failed.");
            throw;
        }
    }
}
