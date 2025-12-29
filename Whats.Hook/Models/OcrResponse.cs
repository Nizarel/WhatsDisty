using System.Text.Json.Serialization;

namespace Whats.Hook.Models
{
    /// <summary>
    /// Response from the OCR contract extraction endpoint.
    /// </summary>
    public class OcrResponse
    {
        [JsonPropertyName("electricity_contract")]
        public string? electricity_contract { get; set; }

        [JsonPropertyName("water_contract")]
        public string? water_contract { get; set; }

        [JsonPropertyName("status")]
        public string? status { get; set; }
    }
}
