// This service is temporarily dormant - media processing disabled
// Suppress obsolete warnings for deprecated ChatRepository methods
#pragma warning disable CS0618

using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Diagnostics;
using Azure;
using Azure.Communication.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;
using FluentValidation;
using Whats.Hook.Constants;
using Whats.Hook.Models;
using Whats.Hook.Repositories;

namespace Whats.Hook.Services
{
    public class MediaService : IDisposable
    {
        private readonly string _connectionString;
        private readonly ChatRepository _chatRepository;
        private readonly NotificationMessagesClient _notificationMessagesClient;
        private readonly ILogger<MediaService> _logger;
        private readonly IMemoryCache _cache;
        private readonly MediaRequestValidator _validator;
        private readonly ResiliencePipeline _resiliencePipeline;
        private readonly MediaProcessingConfiguration _config;
        private bool _disposed = false;

        public MediaService(
            ChatRepository chatRepository, 
            ILogger<MediaService> logger,
            IMemoryCache memoryCache)
        {
            _chatRepository = chatRepository ?? throw new ArgumentNullException(nameof(chatRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
            
            _connectionString = Environment.GetEnvironmentVariable("COMMUNICATION_SERVICES_CONNECTION_STRING")
                ?? throw new ArgumentNullException("COMMUNICATION_SERVICES_CONNECTION_STRING", "Environment variable is not set.");
            
            _notificationMessagesClient = new NotificationMessagesClient(_connectionString);
            _validator = new MediaRequestValidator();
            _config = new MediaProcessingConfiguration(); // Can be injected later for configuration
            
            // Create simplified resilience pipeline for now
            _resiliencePipeline = new ResiliencePipelineBuilder()
                .AddTimeout(_config.ProcessingTimeout)
                .Build();
        }

        public async Task<string?> ProcessImageAsync(WhatsEventType eventData, string storeId, string sessionId, ILogger log)
        {
            var stopwatch = Stopwatch.StartNew();
            
            // Validate input using FluentValidation
            var validationResult = await _validator.ValidateAsync(eventData);
            if (!validationResult.IsValid)
            {
                _logger.LogError("Validation failed for session {SessionId}: {Errors}", 
                    sessionId, string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage)));
                return null;
            }

            if (string.IsNullOrEmpty(storeId) || string.IsNullOrEmpty(sessionId))
            {
                _logger.LogError("StoreId or SessionId is null or empty. StoreId: {StoreId}, SessionId: {SessionId}", storeId, sessionId);
                return null;
            }

            _logger.LogInformation("Processing image with media ID: {MediaId} for session {SessionId}", eventData.media!.id, sessionId);

            try
            {
                // Check cache first if enabled
                var cacheKey = GenerateCacheKey("image", storeId, eventData.media!.id, eventData.media.caption);
                if (_config.EnableCaching && _cache.TryGetValue(cacheKey, out string? cachedResult))
                {
                    _logger.LogInformation("Cache hit for image analysis in session {SessionId}", sessionId);
                    return cachedResult;
                }

                var mediaContentResponse = await _notificationMessagesClient.DownloadMediaAsync(eventData.media.id);
                await using var mediaContentStream = mediaContentResponse.Value;
                
                if (mediaContentStream.Length == 0)
                {
                    _logger.LogWarning("Downloaded media stream is empty for media ID: {MediaId}", eventData.media.id);
                    return null;
                }
                
                _logger.LogInformation("Media content length: {Length} bytes for session {SessionId}", mediaContentStream.Length, sessionId);

                // Use improved image processing with AI optimization
                var imageMode = DetermineImageMode(eventData.media.caption);
                var base64Image = await Services.Utilities.PrepareImageForAIAsync(mediaContentStream, imageMode);
                
                _logger.LogInformation("Base64 image length: {Length} characters for session {SessionId}", base64Image.Length, sessionId);

                var promptText = eventData.media.caption ?? string.Empty;
                // Sanitize / fallback if empty or whitespace
                if (string.IsNullOrWhiteSpace(promptText))
                {
                    promptText = "Describe the product in this image";
                    _logger.LogInformation("Image caption empty; using fallback prompt for session {SessionId}", sessionId);
                }
                else if (promptText.Length > 480)
                {
                    promptText = promptText[..480];
                }
                _logger.LogDebug("Prepared image prompt (len={Len}) for session {SessionId}: {Prompt}", promptText.Length, sessionId, promptText);

                var result = await SendImageAnalysisRequestAsync(storeId, sessionId, promptText, base64Image);

                // Cache successful results
                if (!string.IsNullOrEmpty(result) && _config.EnableCaching)
                {
                    var cacheOptions = new MemoryCacheEntryOptions
                    {
                        SlidingExpiration = TimeSpan.FromMinutes(30),
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(2),
                        Size = EstimateCacheSize(result)
                    };
                    _cache.Set(cacheKey, result, cacheOptions);
                }

                stopwatch.Stop();
                _logger.LogInformation("Image processing completed in {ElapsedMs}ms for session {SessionId}", 
                    stopwatch.ElapsedMilliseconds, sessionId);

                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "An error occurred while processing the image with media ID: {MediaId} for session {SessionId} after {ElapsedMs}ms", 
                    eventData.media.id, sessionId, stopwatch.ElapsedMilliseconds);
                return null;
            }
        }

        /// <summary>
        /// Process invoice image to extract water and electricity contract numbers using OCR.
        /// </summary>
        public async Task<OcrResponse?> ProcessInvoiceOcrAsync(WhatsEventType eventData, ILogger log)
        {
            var stopwatch = Stopwatch.StartNew();

            if (eventData.media == null || string.IsNullOrEmpty(eventData.media.id))
            {
                _logger.LogError("Media information is missing for OCR processing");
                return null;
            }

            _logger.LogInformation("📄 Processing invoice OCR with media ID: {MediaId}", eventData.media.id);

            try
            {
                // Download media from WhatsApp
                var mediaContentResponse = await _notificationMessagesClient.DownloadMediaAsync(eventData.media.id);
                await using var mediaContentStream = mediaContentResponse.Value;
                
                if (mediaContentStream.Length == 0)
                {
                    _logger.LogWarning("Downloaded media stream is empty for media ID: {MediaId}", eventData.media.id);
                    return null;
                }
                
                _logger.LogInformation("📥 Downloaded invoice image: {Length} bytes", mediaContentStream.Length);

                // Determine file name and content type
                var fileName = $"invoice_{eventData.media.id}.jpg";
                var contentType = eventData.media.mimeType ?? "image/jpeg";

                // Call OCR API
                var response = await _chatRepository.SendOcrExtractContractAsync(mediaContentStream, fileName, contentType);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (string.IsNullOrEmpty(responseContent))
                {
                    _logger.LogError("Empty response received from OCR API");
                    return null;
                }

                if (!Services.Utilities.IsValidJson(responseContent))
                {
                    _logger.LogError("Invalid JSON response from OCR API: {Response}", responseContent);
                    return null;
                }

                // Parse OCR response
                var ocrResponse = JsonSerializer.Deserialize<OcrResponse>(responseContent, MediaJsonContext.Default.OcrResponse);
                
                if (ocrResponse == null)
                {
                    _logger.LogError("Failed to deserialize OCR response");
                    return null;
                }

                stopwatch.Stop();
                _logger.LogInformation("✅ OCR processing completed in {ElapsedMs}ms - Water: {Water}, Electricity: {Electricity}", 
                    stopwatch.ElapsedMilliseconds, 
                    ocrResponse.water_contract ?? "none", 
                    ocrResponse.electricity_contract ?? "none");

                return ocrResponse;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "❌ Error during OCR processing for media ID: {MediaId} after {ElapsedMs}ms", 
                    eventData.media.id, stopwatch.ElapsedMilliseconds);
                return null;
            }
        }

        /// <summary>
        /// Process voice message using new Speech-to-Text endpoint.
        /// </summary>
        public async Task<SpeechToTextResponse?> ProcessSpeechToTextAsync(WhatsEventType eventData, ILogger log)
        {
            if (eventData.media == null || string.IsNullOrEmpty(eventData.media.id))
            {
                _logger.LogError("Media information is missing for STT processing");
                return null;
            }

            _logger.LogInformation("🎤 Processing voice STT with media ID: {MediaId}", eventData.media.id);

            try
            {
                var mediaContentResponse = await _notificationMessagesClient.DownloadMediaAsync(eventData.media.id);
                await using var mediaContentStream = mediaContentResponse.Value;

                if (mediaContentStream.Length == 0)
                {
                    _logger.LogWarning("Downloaded voice stream is empty for media ID: {MediaId}", eventData.media.id);
                    return null;
                }

                // Copy to memory stream for HttpClient
                using var memoryStream = new MemoryStream();
                await mediaContentStream.CopyToAsync(memoryStream);
                memoryStream.Position = 0;

                var fileName = $"voice_{eventData.media.id}.wav";
                var contentType = eventData.media.mimeType ?? "audio/wav";

                var response = await _chatRepository.SendSpeechToTextAsync(memoryStream, fileName, contentType);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (string.IsNullOrEmpty(responseContent))
                {
                    _logger.LogError("Empty response received from STT API");
                    return null;
                }

                if (!Services.Utilities.IsValidJson(responseContent))
                {
                    _logger.LogError("Invalid JSON response from STT API: {Response}", responseContent);
                    return null;
                }

                var stt = JsonSerializer.Deserialize<SpeechToTextResponse>(responseContent, MediaJsonContext.Default.SpeechToTextResponse);
                if (stt == null)
                {
                    _logger.LogError("Failed to deserialize STT response");
                    return null;
                }

                _logger.LogInformation("✅ STT success - lang={Lang}, text='{Text}'", stt.language ?? "unknown", stt.text ?? "");
                return stt;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during STT processing for media ID: {MediaId}", eventData.media.id);
                return null;
            }
        }

        // Helper methods for AI optimization
        private AIImageMode DetermineImageMode(string? caption)
        {
            if (string.IsNullOrEmpty(caption))
                return AIImageMode.Standard;

            var lowerCaption = caption.ToLower();
            if (lowerCaption.Contains("detail") || lowerCaption.Contains("analyze") || lowerCaption.Contains("read"))
                return AIImageMode.HighDetail;
            
            if (lowerCaption.Contains("quick") || lowerCaption.Contains("fast"))
                return AIImageMode.Fast;
                
            return AIImageMode.Standard;
        }

        private string GenerateCacheKey(string mediaType, string storeId, string? mediaId, string? caption)
        {
            var contentHash = ComputeContentHash(mediaId ?? "", caption);
            return $"{mediaType}_analysis_{storeId}_{contentHash}";
        }

        private string ComputeContentHash(string mediaId, string? caption)
        {
            using var sha1 = SHA1.Create();
            var input = $"{mediaId}_{caption ?? ""}";
            var hashBytes = sha1.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(hashBytes)[..16]; // First 16 chars for cache key
        }

        private long EstimateCacheSize(string content)
        {
            return System.Text.Encoding.UTF8.GetByteCount(content);
        }

        public async Task<string?> ProcessVoiceAsync(WhatsEventType eventData, string storeId, string sessionId, ILogger log)
        {
            var stopwatch = Stopwatch.StartNew();
            
            // Validate input using FluentValidation
            var validationResult = await _validator.ValidateAsync(eventData);
            if (!validationResult.IsValid)
            {
                _logger.LogError("Validation failed for session {SessionId}: {Errors}", 
                    sessionId, string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage)));
                return null;
            }

            if (string.IsNullOrEmpty(storeId) || string.IsNullOrEmpty(sessionId))
            {
                _logger.LogError("StoreId or SessionId is null or empty. StoreId: {StoreId}, SessionId: {SessionId}", storeId, sessionId);
                return null;
            }

            _logger.LogInformation("Processing voice message with media ID: {MediaId} for session {SessionId}", eventData.media!.id, sessionId);

            try
            {
                // Check cache first if enabled
                var cacheKey = GenerateCacheKey("voice", storeId, eventData.media!.id, null);
                if (_config.EnableCaching && _cache.TryGetValue(cacheKey, out string? cachedResult))
                {
                    _logger.LogInformation("Cache hit for voice analysis in session {SessionId}", sessionId);
                    return cachedResult;
                }

                var mediaContentResponse = await _notificationMessagesClient.DownloadMediaAsync(eventData.media!.id);
                await using var mediaContentStream = mediaContentResponse.Value;
                
                if (mediaContentStream.Length == 0)
                {
                    _logger.LogWarning("Downloaded media stream is empty for media ID: {MediaId}", eventData.media.id);
                    return null;
                }
                
                _logger.LogInformation("Media content length: {Length} bytes for session {SessionId}", mediaContentStream.Length, sessionId);

                // Optimize audio for AI processing
                var optimizedAudio = await AudioProcessor.OptimizeAudioForAIAsync(mediaContentStream, eventData.media.mimeType ?? "audio/wav");
                var optimizedStream = new MemoryStream(optimizedAudio);
                
                var result = await SendVoiceAnalysisRequestAsync(storeId, sessionId, optimizedStream);

                // Cache successful results
                if (!string.IsNullOrEmpty(result) && _config.EnableCaching)
                {
                    var cacheOptions = new MemoryCacheEntryOptions
                    {
                        SlidingExpiration = TimeSpan.FromMinutes(15),
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1),
                        Size = EstimateCacheSize(result)
                    };
                    _cache.Set(cacheKey, result, cacheOptions);
                }

                stopwatch.Stop();
                _logger.LogInformation("Voice processing completed in {ElapsedMs}ms for session {SessionId}", 
                    stopwatch.ElapsedMilliseconds, sessionId);

                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "An error occurred while processing the voice message with media ID: {MediaId} for session {SessionId} after {ElapsedMs}ms", 
                    eventData.media!.id, sessionId, stopwatch.ElapsedMilliseconds);
                return null;
            }
        }

        private async Task<string?> SendImageAnalysisRequestAsync(string storeId, string sessionId, string promptText, string base64Image)
        {
            if (string.IsNullOrEmpty(base64Image))
            {
                _logger.LogError("Base64 image is null or empty for session {SessionId}", sessionId);
                return null;
            }

            var imagePayload = new
            {
                store_id = storeId,  // Changed to snake_case to match API
                session_id = sessionId,  // Changed to snake_case to match API
                prompt_text = promptText ?? string.Empty,  // Changed to snake_case to match API
                image_file = base64Image,  // Keep as base64 for now - will be converted to URL after processing
                metadata = new
                {
                    timestamp = DateTimeOffset.UtcNow,
                    optimizedForAI = true,
                    imageMode = DetermineImageMode(promptText),
                    vision_enabled = true  // Flag for new vision processing
                }
            };

            try
            {
                _logger.LogDebug("Sending image analysis request for session {SessionId} with prompt: {PromptText}", sessionId, promptText);
                
                // Use resilience pipeline for the API call
                var result = await _resiliencePipeline.ExecuteAsync(async cancellationToken =>
                {
                    var response = await _chatRepository.SendImageAsync(imagePayload);
                    var responseContent = await response.Content.ReadAsStringAsync();

                    if (string.IsNullOrEmpty(responseContent))
                    {
                        _logger.LogError("Empty response received from image analysis for session {SessionId}", sessionId);
                        return null;
                    }

                    if (!Services.Utilities.IsValidJson(responseContent))
                    {
                        _logger.LogError("Invalid JSON response received from image analysis for session {SessionId}: {Response}", sessionId, responseContent);
                        return null;
                    }

                    // Parse BusinessInquiryResponse and extract whatsapp_summary
                    var bizResp = JsonSerializer.Deserialize<BusinessInquiryResponse>(responseContent, MediaJsonContext.Default.BusinessInquiryResponse);
                    var summary = bizResp?.whatsapp_summary;
                    if (string.IsNullOrWhiteSpace(summary))
                    {
                        _logger.LogError("Empty whatsapp_summary received from image analysis for session {SessionId}", sessionId);
                        return null;
                    }

                    _logger.LogInformation("Successfully processed image analysis for session {SessionId}", sessionId);
                    return summary;
                }, CancellationToken.None);

                return result;
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "JSON deserialization error during image analysis for session {SessionId}", sessionId);
                return null;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP request error during image analysis for session {SessionId}", sessionId);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during image analysis request for session {SessionId}", sessionId);
                return null;
            }
        }

        private async Task<string?> SendVoiceAnalysisRequestAsync(string storeId, string sessionId, Stream voiceStream)
        {
            if (voiceStream == null)
            {
                _logger.LogError("Voice stream is null for session {SessionId}", sessionId);
                return null;
            }

            var apiUrl = _chatRepository.GetVoiceApiUrl();
            if (string.IsNullOrEmpty(apiUrl))
            {
                _logger.LogError("Voice API URL is null or empty for session {SessionId}", sessionId);
                return null;
            }

            try
            {
                // Use resilience pipeline for the API call
                var result = await _resiliencePipeline.ExecuteAsync(async cancellationToken =>
                {
                    using var content = new MultipartFormDataContent();
                    
                    var fileContent = new StreamContent(voiceStream);
                    // WAV after optimization (16kHz mono PCM)
                    fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
                    content.Add(fileContent, "audioFile", "voiceMessage.wav");

                    content.Add(new StringContent(storeId ?? string.Empty), "store_id");
                    content.Add(new StringContent(sessionId ?? string.Empty), "session_id");
                    
                    // Add processing hints for AI optimization
                    content.Add(new StringContent("true"), "OptimizedForAI");
                    content.Add(new StringContent(DateTimeOffset.UtcNow.ToString("O")), "ProcessingTimestamp");

                    _logger.LogDebug("Sending voice analysis request for session {SessionId} to URL: {ApiUrl}", sessionId, apiUrl);

                    _logger.LogDebug("Posting voice multipart: store_id={StoreId}, session_id={SessionId}, length={Length} bytes, contentType=audio/wav", storeId, sessionId, voiceStream.Length);
                    var response = await _chatRepository.SendVoiceAsync(apiUrl, content);
                    var responseContent = await response.Content.ReadAsStringAsync();

                    if (string.IsNullOrEmpty(responseContent))
                    {
                        _logger.LogError("Empty response received from voice analysis for session {SessionId}", sessionId);
                        return null;
                    }

                    if (!Services.Utilities.IsValidJson(responseContent))
                    {
                        _logger.LogError("Invalid JSON response received from voice analysis for session {SessionId}: {Response}", sessionId, responseContent);
                        return null;
                    }

                    // Parse BusinessInquiryResponse and extract whatsapp_summary (same as image processing)
                    var bizResp = JsonSerializer.Deserialize<BusinessInquiryResponse>(responseContent, MediaJsonContext.Default.BusinessInquiryResponse);
                    var summary = bizResp?.whatsapp_summary ?? bizResp?.transcript;
                    if (string.IsNullOrWhiteSpace(summary))
                    {
                        _logger.LogError("Empty whatsapp_summary/transcript received from voice analysis for session {SessionId}", sessionId);
                        return null;
                    }

                    _logger.LogInformation("Successfully processed voice analysis for session {SessionId}, transcript language: {Language}", 
                        sessionId, bizResp?.transcript_language ?? "unknown");
                    return summary;
                }, CancellationToken.None);

                return result;
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "JSON deserialization error during voice analysis for session {SessionId}", sessionId);
                return null;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP request error during voice analysis for session {SessionId}", sessionId);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during voice analysis request for session {SessionId}", sessionId);
                return null;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                // NotificationMessagesClient doesn't implement IDisposable, so no cleanup needed
                _disposed = true;
            }
        }
    }
}
