using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace DailyChessPuzzleApi.HealthChecks
{
    public sealed class RedisHealthCheck : IHealthCheck
    {
        private readonly IConnectionMultiplexer _redis;

        public RedisHealthCheck(
            IConnectionMultiplexer redis)
        {
            _redis = redis;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // Redis bağlantısının aktif olup olmadığını kontrol et.
                if (!_redis.IsConnected)
                {
                    return HealthCheckResult.Degraded(
                        "Redis connection is unavailable.");
                }

                // Mevcut Redis bağlantısı üzerinden veritabanına eriş.
                var database = _redis.GetDatabase();

                // Redis'e PING göndererek gerçek bağlantıyı doğrula.
                var latency = await database
                    .PingAsync()
                    .WaitAsync(cancellationToken);

                return HealthCheckResult.Healthy(
                    "Redis connection is healthy.",
                    new Dictionary<string, object>
                    {
                        ["latencyMs"] = latency.TotalMilliseconds
                    });
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                return HealthCheckResult.Degraded(
                    "Redis health check failed.",
                    exception);
            }
        }
    }
}