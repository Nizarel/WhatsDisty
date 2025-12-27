using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Whats.Hook.HealthChecks
{
    public class RetailAdvisorApiHealthCheck : IHealthCheck
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<RetailAdvisorApiHealthCheck> _logger;

        public RetailAdvisorApiHealthCheck(IHttpClientFactory httpClientFactory, ILogger<RetailAdvisorApiHealthCheck> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            var baseUrl = Environment.GetEnvironmentVariable("RETAIL_ADVISOR_API_URL");
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return HealthCheckResult.Unhealthy("RETAIL_ADVISOR_API_URL not configured");
            }

            try
            {
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(2);

                var healthUrl = baseUrl.TrimEnd('/') + "/health";
                using var response = await client.GetAsync(healthUrl, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return HealthCheckResult.Healthy("RetailAdvisor API reachable");
                }

                return HealthCheckResult.Unhealthy($"RetailAdvisor API returned status {(int)response.StatusCode}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "RetailAdvisor API health probe failed");
                return HealthCheckResult.Unhealthy("RetailAdvisor API unreachable", ex);
            }
        }
    }
}
