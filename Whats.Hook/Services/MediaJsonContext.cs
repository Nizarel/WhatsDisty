using System.Text.Json.Serialization;
using Whats.Hook.Models;

namespace Whats.Hook.Services
{
    [JsonSerializable(typeof(ChatCompletion))]
    [JsonSerializable(typeof(BusinessInquiryResponse))]
    [JsonSerializable(typeof(WhatsEventType))]
    [JsonSerializable(typeof(Media))]
    [JsonSourceGenerationOptions(
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        GenerationMode = JsonSourceGenerationMode.Metadata,
        UseStringEnumConverter = true)]
    public partial class MediaJsonContext : JsonSerializerContext
    {
    }
}
