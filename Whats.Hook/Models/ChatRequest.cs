namespace Whats.Hook.Models
{
    /// <summary>
    /// Request model for the SRM Chat API endpoint.
    /// POST https://srm-api-recl.azurewebsites.net/api/chat
    /// </summary>
    public class ChatRequest
    {
        public string message { get; set; } = string.Empty;
        public string conversation_id { get; set; } = string.Empty;
        public string language { get; set; } = "fr";
    }
}
