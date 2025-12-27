namespace Whats.Hook.Models
{
    public class SessionResponse
    {
        public bool success { get; set; }
        public bool exists { get; set; }
        public string? session_id { get; set; }
        public string? store_id { get; set; }
        public object? session { get; set; }
        public bool? created_new { get; set; }
        public string? message { get; set; }
    }
}
