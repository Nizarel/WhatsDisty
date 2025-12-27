using System.ComponentModel.DataAnnotations;

namespace Whats.Hook.Models
{
    public class BusinessInquiryRequest
    {
        [Required]
        public string inquiry { get; set; } = string.Empty;
        
        [Required]
        public string store_id { get; set; } = string.Empty;
        
        public string? session_id { get; set; }
        
        public string? business_context { get; set; }
    }
}
