using System.ComponentModel.DataAnnotations;

namespace Whats.Hook.Models
{
    public class MediaProcessingRequest
    {
        [Required]
        public string StoreId { get; set; } = string.Empty;
        
        [Required]
        public string SessionId { get; set; } = string.Empty;
        
        [Required]
        public WhatsEventType EventData { get; set; } = null!;
        
        [Required]
        public List<string> RecipientList { get; set; } = new();
    }

    public class MediaProcessingResponse
    {
        public bool Success { get; set; }
        public string? Result { get; set; }
        public string? ErrorMessage { get; set; }
        public TimeSpan ProcessingTime { get; set; }
        public string MediaType { get; set; } = string.Empty;
        public bool CacheHit { get; set; }
    }

    public class WebhookValidationRequest
    {
        [Required]
        public string ValidationCode { get; set; } = string.Empty;
    }

    public class WebhookProcessingResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public int ProcessedEvents { get; set; }
        public TimeSpan TotalProcessingTime { get; set; }
    }
}
