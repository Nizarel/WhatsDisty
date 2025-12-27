using Microsoft.Extensions.Logging;

namespace Whats.Hook.Services
{
    public static class ConfigValidator
    {
        public static void ValidateEnvironment(ILogger logger)
        {
            var required = new[] {
                "COMMUNICATION_SERVICES_CONNECTION_STRING",
                "CHANNEL_REGISTRATION_ID"
            };
            
            foreach (var env in required)
            {
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(env)))
                {
                    logger.LogError("Missing required environment variable: {EnvVar}", env);
                    throw new InvalidOperationException($"Environment variable '{env}' is required");
                }
            }
        }
    }
}
