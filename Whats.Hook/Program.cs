using Whats.Hook.Repositories;
using Whats.Hook.Services;
using Whats.Hook.HealthChecks;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using System.Text.Json;
using Polly;
using Polly.Extensions.Http;
using Whats.Hook.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Validate critical environment variables early
try 
{
    var tempLogger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger<Program>();
    ConfigValidator.ValidateEnvironment(tempLogger);
}
catch (Exception ex)
{
    Console.WriteLine($"Configuration error: {ex.Message}");
    Environment.Exit(1);
}

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Correlation + HttpClient policies
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<CorrelationHandler>();

// Define simple resilient policies
static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy() => HttpPolicyExtensions
    .HandleTransientHttpError()
    .OrResult(msg => (int)msg.StatusCode == 429)
    .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromMilliseconds(200 * retryAttempt));

static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy() => HttpPolicyExtensions
    .HandleTransientHttpError()
    .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30));

// Add HTTP clients
builder.Services
    .AddHttpClient<ChatRepository>()
    .AddHttpMessageHandler<CorrelationHandler>()
    .AddPolicyHandler(GetRetryPolicy())
    .AddPolicyHandler(GetCircuitBreakerPolicy());

builder.Services
    .AddHttpClient<CatalogStoreService>()
    .AddHttpMessageHandler<CorrelationHandler>()
    .AddPolicyHandler(GetRetryPolicy())
    .AddPolicyHandler(GetCircuitBreakerPolicy());

// Add memory caching for AI optimization
builder.Services.AddMemoryCache(options =>
{
    options.SizeLimit = 100; // Limit cache size
});

// Add FluentValidation
builder.Services.AddValidatorsFromAssemblyContaining<MediaRequestValidator>();

// Add health checks
builder.Services.AddHealthChecks()
    .AddCheck<MediaServiceHealthCheck>("media-service")
    .AddCheck<SessionServiceHealthCheck>("session-service")
    .AddCheck<NotificationServiceHealthCheck>("notification-service")
    .AddCheck<RetailAdvisorApiHealthCheck>("retail-advisor-api", tags: new[] { "ready" });

// Register application services
builder.Services.AddSingleton<ChatRepository>(); // Add this line
builder.Services.AddSingleton<CatalogStoreService>();
builder.Services.AddSingleton<SessionService>();
builder.Services.AddSingleton<MediaService>();
builder.Services.AddSingleton<NotificationService>();

// Add logging configuration
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();
// Note: Add Application Insights package if needed in production
// builder.Services.AddApplicationInsightsTelemetry();

var app = builder.Build();

// Validate configuration at startup
var logger = app.Services.GetRequiredService<ILogger<Program>>();
try
{
    ConfigurationValidator.ValidateConfiguration(logger);
    ConfigurationValidator.LogConfigurationSummary(logger);
}
catch (Exception ex)
{
    logger.LogCritical(ex, "❌ Application startup failed due to configuration issues");
    throw;
}

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Add health check endpoints
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var response = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(x => new
            {
                name = x.Key,
                status = x.Value.Status.ToString(),
                exception = x.Value.Exception?.Message,
                duration = x.Value.Duration.ToString()
            }),
            totalDuration = report.TotalDuration.ToString()
        };
        await context.Response.WriteAsync(JsonSerializer.Serialize(response));
    }
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});

app.UseHttpsRedirection();
app.UseRouting();
app.MapControllers();

logger.LogInformation("🚀 WhatsApp Hook application started successfully");
app.Run();
