namespace Whats.Hook.Models
{
    public class SessionCreateRequest
    {
        public string session_id { get; set; } = string.Empty;
        public string store_id { get; set; } = string.Empty;
        public string? user_name { get; set; }
    }
}
