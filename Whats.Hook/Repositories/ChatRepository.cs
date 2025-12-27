using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Whats.Hook.Models;

namespace Whats.Hook.Repositories
{
    public class ChatRepository
    {
        private readonly HttpClient _httpClient;
        private readonly string _retailAdvisorApiUrl;
        private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerOptions.Default)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        public ChatRepository(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _retailAdvisorApiUrl = Environment.GetEnvironmentVariable("RETAIL_ADVISOR_API_URL") 
                ?? "https://retailadvagent.thankfuldune-81948d3c.eastus2.azurecontainerapps.io";

            // Set a sane default timeout if not configured by DI
            if (_httpClient.Timeout == System.Threading.Timeout.InfiniteTimeSpan)
            {
                _httpClient.Timeout = TimeSpan.FromSeconds(20);
            }
        }

        public async Task<HttpResponseMessage> GetSessionAsync(string sessionId, string storeId)
        {
            var url = $"{_retailAdvisorApiUrl}/session/{sessionId}?store_id={storeId}";
            return await _httpClient.GetAsync(url);
        }

        public async Task<HttpResponseMessage> CreateSessionAsync(SessionCreateRequest request)
        {
            var json = JsonSerializer.Serialize(request, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            return await _httpClient.PostAsync($"{_retailAdvisorApiUrl}/session", content);
        }

        public async Task<HttpResponseMessage> SendBusinessInquiryAsync(BusinessInquiryRequest request)
        {
            var json = JsonSerializer.Serialize(request, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            return await _httpClient.PostAsync($"{_retailAdvisorApiUrl}/business-inquiry", content);
        }

        public async Task<HttpResponseMessage> SendImageAsync(object chatPayload)
        {
            // Use raw naming (no camelCase) so snake_case fields match FastAPI model exactly
            var imageOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = null,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };
            var json = JsonSerializer.Serialize(chatPayload, imageOptions);
            return await _httpClient.PostAsync(_retailAdvisorApiUrl + "/image", new StringContent(json, Encoding.UTF8, "application/json"));
        }

        public async Task<HttpResponseMessage> SendVoiceAsync(string apiUrl, HttpContent content)
        {
            return await _httpClient.PostAsync(apiUrl, content);
        }

        public string GetVoiceApiUrl()
        {
            return _retailAdvisorApiUrl + "/voice";
        }
    }
}
