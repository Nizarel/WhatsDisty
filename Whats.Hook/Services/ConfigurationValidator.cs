namespace Whats.Hook.Services
{
    public static class ConfigurationValidator
    {
        private static readonly string[] RequiredEnvironmentVariables = new[]
        {
            "COMMUNICATION_SERVICES_CONNECTION_STRING",
            "CHANNEL_REGISTRATION_ID"
        };

        // Optional environment variables (have defaults)
        private static readonly string[] OptionalEnvironmentVariables = new[]
        {
            "SRM_API_URL"  // Defaults to https://srm-api-recl.azurewebsites.net
        };

        public static void ValidateConfiguration(ILogger logger)
        {
            var missingVars = new List<string>();
            
            foreach (var varName in RequiredEnvironmentVariables)
            {
                var value = Environment.GetEnvironmentVariable(varName);
                if (string.IsNullOrEmpty(value))
                {
                    missingVars.Add(varName);
                }
                else
                {
                    logger.LogInformation("✅ Environment variable {VarName} is configured", varName);
                }
            }

            if (missingVars.Any())
            {
                var missingVarsList = string.Join(", ", missingVars);
                logger.LogError("❌ Missing required environment variables: {MissingVars}", missingVarsList);
                throw new InvalidOperationException($"Missing required environment variables: {missingVarsList}");
            }

            logger.LogInformation("✅ All required environment variables are configured");
        }

        public static void LogConfigurationSummary(ILogger logger)
        {
            logger.LogInformation("🔧 Configuration Summary:");
            
            foreach (var varName in RequiredEnvironmentVariables)
            {
                var value = Environment.GetEnvironmentVariable(varName);
                var maskedValue = MaskSensitiveValue(varName, value);
                logger.LogInformation("   {VarName}: {Value}", varName, maskedValue);
            }

            // Log optional variables with defaults
            foreach (var varName in OptionalEnvironmentVariables)
            {
                var value = Environment.GetEnvironmentVariable(varName);
                var displayValue = string.IsNullOrEmpty(value) ? "(using default)" : value;
                logger.LogInformation("   {VarName}: {Value}", varName, displayValue);
            }
        }

        private static string MaskSensitiveValue(string varName, string? value)
        {
            if (string.IsNullOrEmpty(value))
                return "❌ NOT SET";

            // Mask sensitive connection strings and keys
            if (varName.Contains("CONNECTION_STRING") || varName.Contains("KEY") || varName.Contains("SECRET"))
            {
                return value.Length > 8 ? 
                    $"{value[..4]}****{value[^4..]}" : 
                    "****";
            }

            return value;
        }
    }
}
