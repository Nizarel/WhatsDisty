//using System.Text.Json.Serialization;

namespace Whats.Hook.Models
{
    public class ChatCompletion
    {
        public string? id { get; set; }
        public string? type { get; set; }
        public string? storeId { get; set; }
        public string? sessionId { get; set; }
        public DateTime timeStamp { get; set; }
        public string? prompt { get; set; }
        public int promptTokens { get; set; }
        public string? completion { get; set; }
        public int completionTokens { get; set; }
    }
}
