using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;

////
namespace Whats.Hook.Services
{
    public enum AIImageMode
    {
        Fast,
        Standard,
        HighDetail
    }

    public static class Utilities
    {
        public static string ConvertImageToBase64(Stream stream)
        {
            using var image = Image.Load(stream);
            
            // AI-optimized processing with ImageSharp
            image.Mutate(x => x
                .Resize(new ResizeOptions
                {
                    Size = new Size(640, 480),
                    Mode = ResizeMode.Max, // Preserve aspect ratio
                    Sampler = KnownResamplers.Lanczos3 // Better quality for AI
                })
                .AutoOrient() // Fix rotation issues
            );
            
            using var ms = new MemoryStream();
            image.SaveAsJpeg(ms, new JpegEncoder 
            { 
                Quality = 85 // Higher quality for AI analysis
            });
            
            return Convert.ToBase64String(ms.ToArray());
        }

        // AI-specific image analysis preparation
        public static async Task<string> PrepareImageForAIAsync(Stream stream, AIImageMode mode = AIImageMode.Standard)
        {
            using var image = await Image.LoadAsync(stream);
            
            var processingOptions = mode switch
            {
                AIImageMode.HighDetail => new Size(1024, 768),
                AIImageMode.Fast => new Size(512, 384),
                _ => new Size(640, 480)
            };
            
            image.Mutate(x => x
                .Resize(new ResizeOptions
                {
                    Size = processingOptions,
                    Mode = ResizeMode.Max,
                    Sampler = KnownResamplers.Lanczos3
                })
                .AutoOrient()
                .Contrast(1.1f) // Slight contrast boost for AI
                .GaussianSharpen(0.5f) // Mild sharpening
            );
            
            using var ms = new MemoryStream();
            var quality = mode == AIImageMode.HighDetail ? 90 : 80;
            await image.SaveAsJpegAsync(ms, new JpegEncoder { Quality = quality });
            
            return Convert.ToBase64String(ms.ToArray());
        }

        public static bool IsValidJson(string strInput)
        {
            if (string.IsNullOrWhiteSpace(strInput)) return false;
            strInput = strInput.Trim();
            if ((strInput.StartsWith("{") && strInput.EndsWith("}")) || // For object
                (strInput.StartsWith("[") && strInput.EndsWith("]")))   // For array
            {
                try
                {
                    var obj = JsonDocument.Parse(strInput);
                    return true;
                }
                catch (JsonException)
                {
                    return false;
                }
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Generates a deterministic GUID-based conversation ID from a phone number.
        /// Uses a namespace-based UUID (v5) to ensure the same phone number always produces the same ID.
        /// </summary>
        public static string GenerateSessionId(string phoneNumber)
        {
            // Create a deterministic GUID based on phone number
            // This ensures the same phone always maps to the same conversation_id
            var sanitized = SanitizePhoneNumber(phoneNumber);
            var bytes = System.Text.Encoding.UTF8.GetBytes(sanitized);
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var hash = sha256.ComputeHash(bytes);
            
            // Use first 16 bytes to create a GUID
            var guidBytes = new byte[16];
            Array.Copy(hash, guidBytes, 16);
            
            return new Guid(guidBytes).ToString();
        }

        public static string SanitizePhoneNumber(string phoneNumber)
        {
            if (string.IsNullOrEmpty(phoneNumber))
                return string.Empty;

            // Remove all non-digit characters (including + symbol)
            var digitsOnly = new string(phoneNumber.Where(char.IsDigit).ToArray());
            
            // Handle country codes - remove common prefixes
            // Mexico: +52 (remove leading 52 if starts with 52)
            // Mexico mobile: +521 (remove leading 521 if starts with 521)
            // US/Canada: +1 (remove leading 1 if 11 digits)
            
            // Check Mexico mobile first (more specific pattern)
            if (digitsOnly.Length == 13 && digitsOnly.StartsWith("521"))
            {
                // Mexico mobile format: +521 followed by 10 digits
                return digitsOnly.Substring(3);
            }
            else if (digitsOnly.Length == 12 && digitsOnly.StartsWith("52"))
            {
                // Mexico landline format: +52 followed by 10 digits  
                return digitsOnly.Substring(2);
            }
            else if (digitsOnly.Length == 11 && digitsOnly.StartsWith("1"))
            {
                // US/Canada format: +1 followed by 10 digits
                return digitsOnly.Substring(1);
            }
            
            return digitsOnly;
        }
    }
}
