using System.Text.Json.Serialization;

namespace Whats.Hook.Models
{
    public class SpeechToTextResponse
    {
        [JsonPropertyName("language")]
        public string? language { get; set; }

        [JsonPropertyName("status")]
        public string? status { get; set; }

        [JsonPropertyName("text")]
        public string? text { get; set; }
    }
}
