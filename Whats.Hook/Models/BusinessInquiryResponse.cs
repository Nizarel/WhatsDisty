namespace Whats.Hook.Models
{
    public class BusinessInquiryResponse
    {
        public bool success { get; set; }
        public string? whatsapp_summary { get; set; }
        public string? sql_query { get; set; }
        public int? row_count { get; set; }
        public float? execution_time { get; set; }
        public int? character_count { get; set; }
        public bool? is_whatsapp_optimized { get; set; }
        public bool? cache_hit { get; set; }
        public string? error { get; set; }
        
        // Speech transcription fields
        public string? transcript { get; set; }
        public string? transcript_language { get; set; }
        public string? transcription_engine { get; set; }
        public double? transcription_latency_ms { get; set; }
        public double? audio_duration_sec { get; set; }
    }
}
