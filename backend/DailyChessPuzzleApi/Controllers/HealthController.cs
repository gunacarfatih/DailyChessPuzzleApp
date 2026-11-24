using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DailyChessPuzzleApi.Controllers
{
    [ApiController]
    [Route("health")]
    [AllowAnonymous]
    public sealed class HealthController : ControllerBase
    {
        private readonly HealthCheckService _healthCheckService;

        public HealthController(
            HealthCheckService healthCheckService)
        {
            _healthCheckService = healthCheckService;
        }

        // Sistemin genel sağlık durumunu kontrol eder.
        [HttpGet]
        public async Task<IActionResult> GetHealth(
            CancellationToken cancellationToken)
        {
            var report = await _healthCheckService
                .CheckHealthAsync(cancellationToken);

            return CreateHealthResponse(report);
        }

        // API uygulamasının ayakta olup olmadığını kontrol eder.
        [HttpGet("live")]
        public IActionResult GetLiveness()
        {
            return Ok(new
            {
                status = "Healthy",
                timestamp = DateTime.UtcNow
            });
        }

        // Sistemin istek karşılamaya hazır olup olmadığını kontrol eder.
        [HttpGet("ready")]
        public async Task<IActionResult> GetReadiness(
            CancellationToken cancellationToken)
        {
            var report = await _healthCheckService
                .CheckHealthAsync(
                    registration => registration.Tags.Contains("ready"),
                    cancellationToken);

            return CreateHealthResponse(report);
        }

        // Sağlık kontrolü sonuçlarını güvenli JSON yanıtına dönüştürür.
        private IActionResult CreateHealthResponse(
            HealthReport report)
        {
            var response = new
            {
                status = report.Status.ToString(),

                timestamp = DateTime.UtcNow,

                durationMs = Math.Round(
                    report.TotalDuration.TotalMilliseconds, 2),

                checks = report.Entries.Select(entry => new
                {
                    name = entry.Key,

                    status = entry.Value.Status.ToString(),

                    durationMs = Math.Round(
                        entry.Value.Duration.TotalMilliseconds, 2)
                })
            };

            return report.Status == HealthStatus.Unhealthy
                ? StatusCode(
                    StatusCodes.Status503ServiceUnavailable,
                    response)
                : Ok(response);
        }
    }
}