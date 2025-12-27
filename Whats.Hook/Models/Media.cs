using System.Text.Json.Serialization;

namespace Whats.Hook.Models
{
    public class Media
    {
        [JsonPropertyName("mimeType")]
        public string? mimeType { get; set; }

        [JsonPropertyName("id")]
        public string? id { get; set; }

        [JsonPropertyName("caption")]
        public string? caption { get; set; }
    
    }
}
