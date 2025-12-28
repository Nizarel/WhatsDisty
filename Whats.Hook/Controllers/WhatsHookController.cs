using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Azure.Messaging.EventGrid;
using Azure.Messaging.EventGrid.SystemEvents;
using Whats.Hook.Models;
using Whats.Hook.Services;

namespace Whats.Hook.Controllers
{
    [Route("webhook")]
    [ApiController]
    public class WhatsHookController : ControllerBase
    {
        private readonly ILogger<WhatsHookController> _logger;
        private readonly SessionService _sessionService;
        private readonly MediaService _mediaService; // Kept for future use
        private readonly NotificationService _notificationService;
        private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
        
        // In-memory storage for conversation IDs per phone number
        // In production, consider using Redis or database for persistence
        private static readonly Dictionary<string, string> _phoneConversationMap = new();

        private bool EventTypeSubscriptionValidation
            => HttpContext.Request.Headers["aeg-event-type"].FirstOrDefault() == "SubscriptionValidation";

        private bool EventTypeNotification
            => HttpContext.Request.Headers["aeg-event-type"].FirstOrDefault() == "Notification";

        public WhatsHookController(
            ILogger<WhatsHookController> logger,
            SessionService sessionService,
            MediaService mediaService,
            NotificationService notificationService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
            _mediaService = mediaService ?? throw new ArgumentNullException(nameof(mediaService));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        }

        [HttpGet("health")]
        public IActionResult Health()
        {
            return Ok(new { 
                status = "healthy", 
                timestamp = DateTime.UtcNow,
                version = "2.0",
                api = "SRM Chat API",
                services = new { session = "ready", notification = "ready" }
            });
        }

        [HttpOptions]
        public IActionResult Options()
        {
            var headers = HttpContext.Request.Headers;
            var webhookRequestOrigin = headers["WebHook-Request-Origin"].FirstOrDefault();
            var webhookRequestCallback = headers["WebHook-Request-Callback"].FirstOrDefault();
            HttpContext.Response.Headers.Append("WebHook-Allowed-Rate", "*");
            HttpContext.Response.Headers.Append("WebHook-Allowed-Origin", webhookRequestOrigin ?? "*");
            if (!string.IsNullOrEmpty(webhookRequestCallback))
            {
                HttpContext.Response.Headers.Append("WebHook-Allowed-Callback", webhookRequestCallback);
            }
            return Ok();
        }

        [HttpPost]
        public async Task<IActionResult> Post()
        {
            var headers = HttpContext.Request.Headers;
            var eventType = headers["aeg-event-type"].FirstOrDefault();
            _logger.LogInformation("/webhook POST received. aeg-event-type={EventType}; Content-Type={ContentType}; Length={Length}",
                eventType ?? "(null)", Request.ContentType ?? "(null)", Request.ContentLength?.ToString() ?? "unknown");
            string jsonContent;
            using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
            {
                jsonContent = await reader.ReadToEndAsync();
            }
            _logger.LogDebug("Raw EventGrid payload: {PayloadSnippet}...", jsonContent.Length > 500 ? jsonContent.Substring(0, 500) : jsonContent);
            if (eventType == "SubscriptionValidation")
            {
                _logger.LogInformation("Handling SubscriptionValidation event");
                return await HandleValidation(jsonContent);
            }
            else if (eventType == "Notification")
            {
                _logger.LogInformation("Handling Notification event");
                return await HandleGridEvents(jsonContent);
            }
            else
            {
                _logger.LogWarning("Unsupported or missing aeg-event-type header. Attempting generic processing. aeg-event-type={EventType}", eventType ?? "(null)");
                try
                {
                    return await HandleGridEvents(jsonContent);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed generic processing of webhook payload");
                }
            }
            return BadRequest();
        }

        private async Task<JsonResult> HandleValidation(string jsonContent)
        {
            await Task.CompletedTask;
            var eventGridEvent = JsonSerializer.Deserialize<EventGridEvent[]>(jsonContent, _jsonOptions)?.FirstOrDefault();
            if (eventGridEvent == null)
            {
                return new JsonResult(new { error = "Invalid event data" }) { StatusCode = 400 };
            }
            var eventData = JsonSerializer.Deserialize<SubscriptionValidationEventData>(eventGridEvent.Data.ToString()!, _jsonOptions);
            var responseData = new Whats.Hook.Models.SubscriptionValidationResponse
            {
                ValidationResponse = eventData?.ValidationCode
            };
            return new JsonResult(responseData);
        }

        private async Task<IActionResult> HandleGridEvents(string jsonContent)
        {
            _logger.LogInformation("📨 Parsing EventGrid events");
            
            var eventGridEvents = JsonSerializer.Deserialize<EventGridEvent[]>(jsonContent, _jsonOptions);
            if (eventGridEvents == null)
            {
                _logger.LogError("❌ Failed to parse EventGrid events - NULL result");
                return BadRequest("Invalid event data");
            }
            _logger.LogInformation("📨 Processing {Count} EventGrid events", eventGridEvents.Length);

            foreach (var eventGridEvent in eventGridEvents)
            {
                _logger.LogInformation("📨 Event: id={Id}, type={Type}, subject={Subject}", 
                    eventGridEvent.Id, eventGridEvent.EventType, eventGridEvent.Subject);
                    
                if (eventGridEvent.EventType.Equals("microsoft.communication.advancedmessagereceived", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("✅ WhatsApp message received - processing");
                    await ProcessWhatsAppMessage(eventGridEvent);
                }
                else
                {
                    _logger.LogDebug("⏭️ Ignored event type: {Type}", eventGridEvent.EventType);
                }
            }

            return Ok();
        }

        private async Task ProcessWhatsAppMessage(EventGridEvent eventGridEvent)
        {
            _logger.LogInformation("🔄 ProcessWhatsAppMessage started - EventId: {EventId}", eventGridEvent?.Id ?? "null");

            try
            {
                if (eventGridEvent == null)
                {
                    _logger.LogError("❌ EventGridEvent is null.");
                    throw new ArgumentNullException(nameof(eventGridEvent));
                }

                var jsonElement = JsonSerializer.Deserialize<JsonElement>(eventGridEvent.Data.ToString()!);
                
                _logger.LogDebug("🔍 Raw event data: {EventData}", eventGridEvent.Data.ToString());

                var eventData = new WhatsEventType
                {
                    from = GetJsonValue(jsonElement, "from"),
                    to = GetJsonValue(jsonElement, "to"),
                    content = GetJsonValue(jsonElement, "body") ?? GetJsonValue(jsonElement, "content"),
                    channelType = GetJsonValue(jsonElement, "channelType") ?? "whatsapp",
                    receivedTimestamp = eventGridEvent.EventTime.DateTime
                };

                if (string.IsNullOrEmpty(eventData.from))
                {
                    _logger.LogError("❌ Event data 'from' field is null or empty.");
                    throw new InvalidOperationException("Missing 'from' field in event data.");
                }

                _logger.LogInformation("📱 Received WhatsApp message from: {PhoneNumber}", eventData.from);

                var recipientList = new List<string> { FormatPhoneNumberForWhatsApp(eventData.from) };

                _logger.LogInformation("💬 Processing message from PhoneNumber: {PhoneNumber}", eventData.from);

                // Check for media (currently disabled but logged)
                var hasMedia = CheckForMedia(jsonElement, eventData);
                if (hasMedia)
                {
                    _logger.LogWarning("📸 Media message detected but media processing is currently disabled. Processing as text only.");
                    // In future, re-enable: await ProcessMediaMessage(eventData, conversationId, recipientList);
                }

                // Process as text message using new SRM Chat API
                await ProcessTextMessage(eventData, eventData.from, recipientList);

                _logger.LogInformation("✅ WhatsApp message processed successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ An error occurred while processing the event.");
                throw;
            }
        }

        private bool CheckForMedia(JsonElement jsonElement, WhatsEventType eventData)
        {
            // Check multiple possible media property patterns
            if (jsonElement.TryGetProperty("mediaUri", out _) && jsonElement.TryGetProperty("mediaContentType", out var mediaContentType))
            {
                _logger.LogDebug("📸 Media detected (Pattern 1) - Type: {Type}", mediaContentType.GetString());
                return true;
            }
            if (jsonElement.TryGetProperty("attachments", out var attachments) && attachments.ValueKind == JsonValueKind.Array && attachments.GetArrayLength() > 0)
            {
                _logger.LogDebug("📎 Attachments detected (Pattern 2)");
                return true;
            }
            if (jsonElement.TryGetProperty("media", out _))
            {
                _logger.LogDebug("📱 Media object detected (Pattern 3)");
                return true;
            }
            return false;
        }

        private string? GetJsonValue(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var property))
            {
                return property.GetString();
            }
            return null;
        }

        /// <summary>
        /// Formats phone number for WhatsApp notifications using Azure Communication Services.
        /// ACS expects international format with + prefix for WhatsApp.
        /// </summary>
        private string FormatPhoneNumberForWhatsApp(string phoneNumber)
        {
            if (string.IsNullOrEmpty(phoneNumber))
                return string.Empty;

            // If already has + prefix, return as-is
            if (phoneNumber.StartsWith("+"))
                return phoneNumber;

            // Remove any non-digit characters
            var digitsOnly = new string(phoneNumber.Where(char.IsDigit).ToArray());
            
            // Add + prefix for international format
            return $"+{digitsOnly}";
        }

        private async Task ProcessTextMessage(WhatsEventType eventData, string phoneNumber, List<string> recipientList)
        {
            if (string.IsNullOrEmpty(eventData.content))
            {
                _logger.LogWarning("⚠️ Event data 'content' field is null or empty.");
                return;
            }

            _logger.LogInformation("💬 Sending message to SRM Chat API: {Content}", 
                eventData.content.Length > 100 ? eventData.content.Substring(0, 100) + "..." : eventData.content);

            // Get existing conversation_id for this phone number, or null for new conversation
            string? conversationId = null;
            lock (_phoneConversationMap)
            {
                _phoneConversationMap.TryGetValue(phoneNumber, out conversationId);
            }

            _logger.LogInformation("📞 Phone: {Phone}, ConversationId: {ConversationId}", 
                phoneNumber, conversationId ?? "null (new conversation)");

            // Use the new simplified ProcessChatAsync with SRM API
            var (response, returnedConversationId) = await _sessionService.ProcessChatAsync(conversationId, eventData.content, _logger, "fr");
            
            // Store the conversation_id for future messages from this phone number
            if (!string.IsNullOrEmpty(returnedConversationId))
            {
                lock (_phoneConversationMap)
                {
                    _phoneConversationMap[phoneNumber] = returnedConversationId;
                }
                _logger.LogInformation("💾 Stored conversation_id: {ConversationId} for phone: {Phone}", 
                    returnedConversationId, phoneNumber);
            }
            
            if (!string.IsNullOrEmpty(response))
            {
                _logger.LogInformation("📤 Sending response to WhatsApp: {Response}", 
                    response.Length > 100 ? response.Substring(0, 100) + "..." : response);
                await _notificationService.SendTextNotification(response, recipientList, _logger);
            }
            else
            {
                _logger.LogWarning("⚠️ No response received from SRM Chat API.");
            }
        }

        #region Deprecated - Kept for potential future use

        [Obsolete("Media processing temporarily disabled.")]
        private async Task ProcessMediaMessage(WhatsEventType eventData, string storeId, string sessionId, List<string> recipientList)
        {
            var imageMimeTypes = new HashSet<string> { "image/jpeg", "image/png" };
            var voiceMimeTypes = new HashSet<string>(Whats.Hook.Constants.MediaTypes.VoiceMimeTypes)
            {
                "video/mp4"
            };

            if (eventData.media?.mimeType != null && (eventData.media.mimeType.StartsWith("image/") || imageMimeTypes.Contains(eventData.media.mimeType)))
            {
                var imgCompletion = await _mediaService.ProcessImageAsync(eventData, storeId, sessionId, _logger);
                if (!string.IsNullOrEmpty(imgCompletion))
                {
                    await _notificationService.SendTextNotification(imgCompletion, recipientList, _logger);
                }
            }
            else if (eventData.media?.mimeType != null && (eventData.media.mimeType.StartsWith("audio/") || voiceMimeTypes.Contains(eventData.media.mimeType)))
            {
                _logger.LogInformation("Processing voice message with MIME type: {MimeType}", eventData.media.mimeType ?? "null");
                var voiceCompletion = await _mediaService.ProcessVoiceAsync(eventData, storeId, sessionId, _logger);
                if (!string.IsNullOrEmpty(voiceCompletion))
                {
                    await _notificationService.SendTextNotification(voiceCompletion, recipientList, _logger);
                }
                else
                {
                    _logger.LogError("Voice processing returned an empty result.");
                }
            }
            else
            {
                _logger.LogWarning("Unsupported media type: {MimeType}", eventData.media?.mimeType ?? "null");
            }
        }

        #endregion
    }
}