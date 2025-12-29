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
        private readonly string _srmApiUrl;
        private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerOptions.Default)
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        public ChatRepository(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _srmApiUrl = Environment.GetEnvironmentVariable("SRM_API_URL") 
                ?? "https://srm-api-recl.azurewebsites.net";

            // Set a sane default timeout if not configured by DI
            if (_httpClient.Timeout == System.Threading.Timeout.InfiniteTimeSpan)
            {
                _httpClient.Timeout = TimeSpan.FromSeconds(30);
            }
        }

        /// <summary>
        /// Send a chat message to the SRM API.
        /// POST /api/chat with {message, conversation_id, language}
        /// </summary>
        public async Task<HttpResponseMessage> SendChatAsync(ChatRequest request)
        {
            var json = JsonSerializer.Serialize(request, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            return await _httpClient.PostAsync($"{_srmApiUrl}/api/chat", content);
        }

        /// <summary>
        /// Convert speech audio to text using Speech-to-Text API.
        /// POST /api/speech-to-text with multipart/form-data 'audio' field.
        /// Returns JSON {language, status, text}.
        /// </summary>
        public async Task<HttpResponseMessage> SendSpeechToTextAsync(Stream audioStream, string fileName, string contentType)
        {
            using var content = new MultipartFormDataContent();
            var streamContent = new StreamContent(audioStream);
            
            // Extract base media type without parameters (e.g., "audio/ogg; codecs=opus" -> "audio/ogg")
            // MediaTypeHeaderValue doesn't accept content types with parameters in constructor
            var baseContentType = contentType.Split(';')[0].Trim();
            streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(baseContentType);
            content.Add(streamContent, "audio", fileName);

            return await _httpClient.PostAsync($"{_srmApiUrl}/api/speech-to-text", content);
        }

        /// <summary>
        /// Synthesize speech audio from text.
        /// POST /api/synthesize/speech with JSON {text}.
        /// Returns binary audio stream.
        /// </summary>
        public async Task<HttpResponseMessage> SendTextToSpeechAsync(string text)
        {
            var payload = new { text };
            var json = JsonSerializer.Serialize(payload, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            return await _httpClient.PostAsync($"{_srmApiUrl}/api/synthesize/speech", content);
        }

        /// <summary>
        /// Extract water and electricity contract numbers from invoice image.
        /// POST /api/ocr/extract-contract with multipart/form-data file upload.
        /// </summary>
        public async Task<HttpResponseMessage> SendOcrExtractContractAsync(Stream imageStream, string fileName, string contentType)
        {
            using var content = new MultipartFormDataContent();
            var streamContent = new StreamContent(imageStream);
            streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
            content.Add(streamContent, "file", fileName);

            return await _httpClient.PostAsync($"{_srmApiUrl}/api/ocr/extract-contract", content);
        }

        /// <summary>
        /// Convenience overload to send a chat message with explicit parameters.
        /// conversation_id can be null for new conversations - API will create and return one.
        /// </summary>
        public async Task<HttpResponseMessage> SendChatAsync(string message, string? conversationId, string language = "fr")
        {
            var request = new ChatRequest
            {
                message = message,
                conversation_id = conversationId,
                language = language
            };
            return await SendChatAsync(request);
        }

        #region Deprecated - Kept for potential future use

        [Obsolete("Use SendChatAsync instead. Kept for backward compatibility.")]
        public async Task<HttpResponseMessage> SendBusinessInquiryAsync(BusinessInquiryRequest request)
        {
            var json = JsonSerializer.Serialize(request, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            return await _httpClient.PostAsync($"{_srmApiUrl}/business-inquiry", content);
        }

        [Obsolete("Session management removed. Use conversation_id (GUID) instead.")]
        public async Task<HttpResponseMessage> GetSessionAsync(string sessionId, string storeId)
        {
            var url = $"{_srmApiUrl}/session/{sessionId}?store_id={storeId}";
            return await _httpClient.GetAsync(url);
        }

        [Obsolete("Session management removed. Use conversation_id (GUID) instead.")]
        public async Task<HttpResponseMessage> CreateSessionAsync(SessionCreateRequest request)
        {
            var json = JsonSerializer.Serialize(request, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            return await _httpClient.PostAsync($"{_srmApiUrl}/session", content);
        }

        [Obsolete("Image processing temporarily disabled.")]
        public async Task<HttpResponseMessage> SendImageAsync(object chatPayload)
        {
            var imageOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = null,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };
            var json = JsonSerializer.Serialize(chatPayload, imageOptions);
            return await _httpClient.PostAsync(_srmApiUrl + "/image", new StringContent(json, Encoding.UTF8, "application/json"));
        }

        [Obsolete("Voice processing temporarily disabled.")]
        public async Task<HttpResponseMessage> SendVoiceAsync(string apiUrl, HttpContent content)
        {
            return await _httpClient.PostAsync(apiUrl, content);
        }

        [Obsolete("Voice processing temporarily disabled.")]
        public string GetVoiceApiUrl()
        {
            return _srmApiUrl + "/voice";
        }

        #endregion
    }
}
