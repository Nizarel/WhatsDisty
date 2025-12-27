using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Whats.Hook.Infrastructure
{
    /// <summary>
    /// DelegatingHandler that ensures outbound HTTP requests carry a correlation ID for traceability.
    /// </summary>
    public class CorrelationHandler : DelegatingHandler
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public CorrelationHandler(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var httpContext = _httpContextAccessor.HttpContext;

            // Try to reuse inbound correlation/request identifiers when available
            string? corr = httpContext?.TraceIdentifier;
            if (string.IsNullOrEmpty(corr) && Activity.Current != null)
            {
                corr = Activity.Current.TraceId.ToString();
            }
            if (string.IsNullOrEmpty(corr))
            {
                corr = System.Guid.NewGuid().ToString();
            }
            var correlationId = corr;

            if (!request.Headers.Contains("x-correlation-id"))
            {
                request.Headers.Add("x-correlation-id", correlationId);
            }

        if (httpContext != null && httpContext.Request.Headers.TryGetValue("x-request-id", out var reqId))
            {
                if (!request.Headers.Contains("x-request-id"))
                {
            request.Headers.Add("x-request-id", reqId.ToString());
                }
            }

            return await base.SendAsync(request, cancellationToken);
        }
    }
}
