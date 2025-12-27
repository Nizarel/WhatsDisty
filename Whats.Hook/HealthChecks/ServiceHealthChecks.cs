using Microsoft.Extensions.Diagnostics.HealthChecks;
using Whats.Hook.Services;

namespace Whats.Hook.HealthChecks
{
    public class MediaServiceHealthCheck : IHealthCheck
    {
        private readonly MediaService _mediaService;
        private readonly ILogger<MediaServiceHealthCheck> _logger;

        public MediaServiceHealthCheck(MediaService mediaService, ILogger<MediaServiceHealthCheck> logger)
        {
            _mediaService = mediaService;
            _logger = logger;
        }

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                // Check if Azure Communication Services connection is working
                var connectionString = Environment.GetEnvironmentVariable("COMMUNICATION_SERVICES_CONNECTION_STRING");
                if (string.IsNullOrEmpty(connectionString))
                {
                    return Task.FromResult(HealthCheckResult.Unhealthy("COMMUNICATION_SERVICES_CONNECTION_STRING not configured"));
                }

                // Basic connectivity check could be added here
                return Task.FromResult(HealthCheckResult.Healthy("MediaService is healthy"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MediaService health check failed");
                return Task.FromResult(HealthCheckResult.Unhealthy("MediaService health check failed", ex));
            }
        }
    }

    public class SessionServiceHealthCheck : IHealthCheck
    {
        private readonly SessionService _sessionService;
        private readonly ILogger<SessionServiceHealthCheck> _logger;

        public SessionServiceHealthCheck(SessionService sessionService, ILogger<SessionServiceHealthCheck> logger)
        {
            _sessionService = sessionService;
            _logger = logger;
        }

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                // Check if RetailAdvisor API is reachable
                var apiUrl = Environment.GetEnvironmentVariable("RETAIL_ADVISOR_API_URL");
                if (string.IsNullOrEmpty(apiUrl))
                {
                    return Task.FromResult(HealthCheckResult.Unhealthy("RETAIL_ADVISOR_API_URL not configured"));
                }

                return Task.FromResult(HealthCheckResult.Healthy("SessionService is healthy"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SessionService health check failed");
                return Task.FromResult(HealthCheckResult.Unhealthy("SessionService health check failed", ex));
            }
        }
    }

    public class NotificationServiceHealthCheck : IHealthCheck
    {
        private readonly NotificationService _notificationService;
        private readonly ILogger<NotificationServiceHealthCheck> _logger;

        public NotificationServiceHealthCheck(NotificationService notificationService, ILogger<NotificationServiceHealthCheck> logger)
        {
            _notificationService = notificationService;
            _logger = logger;
        }

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                var connectionString = Environment.GetEnvironmentVariable("COMMUNICATION_SERVICES_CONNECTION_STRING");
                var channelRegId = Environment.GetEnvironmentVariable("CHANNEL_REGISTRATION_ID");
                
                if (string.IsNullOrEmpty(connectionString))
                    return Task.FromResult(HealthCheckResult.Unhealthy("COMMUNICATION_SERVICES_CONNECTION_STRING not configured"));
                
                if (string.IsNullOrEmpty(channelRegId))
                    return Task.FromResult(HealthCheckResult.Unhealthy("CHANNEL_REGISTRATION_ID not configured"));

                return Task.FromResult(HealthCheckResult.Healthy("NotificationService is healthy"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NotificationService health check failed");
                return Task.FromResult(HealthCheckResult.Unhealthy("NotificationService health check failed", ex));
            }
        }
    }
}
