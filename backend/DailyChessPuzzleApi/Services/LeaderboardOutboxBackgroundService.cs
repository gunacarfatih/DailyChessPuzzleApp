using DailyChessPuzzleApi.Services.Interfaces;

namespace DailyChessPuzzleApi.Services
{
    public class LeaderboardOutboxBackgroundService
        : BackgroundService
    {
        private static readonly TimeSpan PollingInterval =
            TimeSpan.FromSeconds(10);

        private readonly IServiceScopeFactory _scopeFactory;

        private readonly ILogger<LeaderboardOutboxBackgroundService>
            _logger;

        private readonly LeaderboardOutboxSignal _signal;

        public LeaderboardOutboxBackgroundService(
            IServiceScopeFactory scopeFactory,
            ILogger<LeaderboardOutboxBackgroundService> logger,
            LeaderboardOutboxSignal signal)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _signal = signal;
        }

        protected override async Task ExecuteAsync(
            CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "Leaderboard Outbox Background Service başlatıldı.");

            DateOnly? lastCleanupDate = null;

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope =
                        _scopeFactory.CreateScope();

                    var outboxService = scope.ServiceProvider
                        .GetRequiredService<ILeaderboardOutboxService>();

                    var hasMore =
                        await outboxService.ProcessPendingAsync(
                            stoppingToken);

                    var today =
                        DateOnly.FromDateTime(DateTime.UtcNow);

                    if (lastCleanupDate != today)
                    {
                        await outboxService.CleanupCompletedAsync(
                            stoppingToken);

                        lastCleanupDate = today;
                    }

                    if (hasMore)
                    {
                        continue;
                    }
                }
                catch (OperationCanceledException)
                    when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Leaderboard Outbox Background Service işleminde hata oluştu.");
                }

                try
                {
                    await _signal.WaitAsync(
                        PollingInterval,
                        stoppingToken);
                }
                catch (OperationCanceledException)
                    when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }

            _logger.LogInformation(
                "Leaderboard Outbox Background Service durduruldu.");
        }
    }
}