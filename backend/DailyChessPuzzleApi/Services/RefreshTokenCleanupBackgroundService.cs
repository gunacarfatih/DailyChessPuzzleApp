using DailyChessPuzzleApi.Data;
using Microsoft.EntityFrameworkCore;

namespace DailyChessPuzzleApi.Services;

public sealed class RefreshTokenCleanupBackgroundService
    : BackgroundService
{
    private static readonly TimeSpan CleanupInterval =
        TimeSpan.FromHours(6);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RefreshTokenCleanupBackgroundService> _logger;

    public RefreshTokenCleanupBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<RefreshTokenCleanupBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        await CleanupAsync(stoppingToken);

        using var timer =
            new PeriodicTimer(CleanupInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(
                       stoppingToken))
            {
                await CleanupAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
            // Uygulama normal şekilde kapanıyor.
        }
    }

    private async Task CleanupAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await using var scope =
                _scopeFactory.CreateAsyncScope();

            var dbContext = scope.ServiceProvider
                .GetRequiredService<ApplicationDbContext>();

            var now = DateTime.UtcNow;

            var deletedCount =
                await dbContext.RefreshTokens
                    .Where(token =>
                        token.ExpiresAtUtc <= now)
                    .ExecuteDeleteAsync(cancellationToken);

            if (deletedCount > 0)
            {
                _logger.LogInformation(
                    "Expired refresh tokens deleted. Count: {Count}",
                    deletedCount);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Expired refresh token cleanup failed.");
        }
    }
}