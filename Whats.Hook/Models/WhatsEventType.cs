using System.Text.Json.Serialization;

namespace Whats.Hook.Models
{
    public class WhatsEventType
    {
        [JsonPropertyName("content")]
        public string? content { get; set; }

        [JsonPropertyName("channelType")]
        public string? channelType { get; set; }

        [JsonPropertyName("from")]
        public string? from { get; set; }

        [JsonPropertyName("to")]
        public string? to { get; set; }

        [JsonPropertyName("receivedTimestamp")]
        public DateTime receivedTimestamp { get; set; }

        [JsonPropertyName("media")]
        public Media? media { get; set; }
    }

    public class SubscriptionValidationResponse
    {
        [JsonPropertyName("validationResponse")]
        public string? ValidationResponse { get; set; }
    }
}