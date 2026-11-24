using DailyChessPuzzleApi.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DailyChessPuzzleApi.HealthChecks
{
    public sealed class PostgreSqlHealthCheck : IHealthCheck
    {
        private readonly ApplicationDbContext _context;

        public PostgreSqlHealthCheck(
            ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // PostgreSQL bağlantısının kullanılabilirliğini kontrol et.
                var canConnect = await _context.Database
                    .CanConnectAsync(cancellationToken);

                if (!canConnect)
                {
                    return HealthCheckResult.Unhealthy(
                        "PostgreSQL connection is unavailable.");
                }

                return HealthCheckResult.Healthy(
                    "PostgreSQL connection is healthy.");
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                return HealthCheckResult.Unhealthy(
                    "PostgreSQL health check failed.",
                    exception);
            }
        }
    }
}