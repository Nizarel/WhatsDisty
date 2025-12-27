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
        private readonly MediaService _mediaService;
        private readonly NotificationService _notificationService;
        private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

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
                version = "1.0",
                services = new { media = "ready", session = "ready", notification = "ready" }
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
                // Try to parse as events anyway (EventGrid sends an array)
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
            var eventData = JsonSerializer.Deserialize<SubscriptionValidationEventData>(eventGridEvent.Data.ToString(), _jsonOptions);
            var responseData = new Whats.Hook.Models.SubscriptionValidationResponse
            {
                ValidationResponse = eventData?.ValidationCode
            };
            return new JsonResult(responseData);
        }

        private async Task<IActionResult> HandleGridEvents(string jsonContent)
        {
            _logger.LogCritical("🚨 PARSING EVENTGRID EVENTS FROM: {JsonContent}", jsonContent);
            
            var eventGridEvents = JsonSerializer.Deserialize<EventGridEvent[]>(jsonContent, _jsonOptions);
            if (eventGridEvents == null)
            {
                _logger.LogCritical("❌ FAILED TO PARSE EVENTGRID EVENTS - NULL RESULT");
                return BadRequest("Invalid event data");
            }
            _logger.LogCritical("🚨 PROCESSING {Count} EVENTGRID EVENTS", eventGridEvents.Length);

            foreach (var eventGridEvent in eventGridEvents)
            {
                _logger.LogCritical("🚨 EVENT: id={Id}, type={Type}, subject={Subject}, eventTime={EventTime}", 
                    eventGridEvent.Id, eventGridEvent.EventType, eventGridEvent.Subject, eventGridEvent.EventTime);
                    
                if (eventGridEvent.EventType.Equals("microsoft.communication.advancedmessagereceived", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogCritical("✅ MATCHED WHATSAPP EVENT TYPE - PROCESSING MESSAGE");
                    await ProcessWhatsAppMessage(eventGridEvent);
                }
                else
                {
                    _logger.LogCritical("❌ IGNORED EVENT TYPE: {Type} (not WhatsApp advanced message)", eventGridEvent.EventType);
                }
            }

            return Ok();
        }

        private async Task ProcessWhatsAppMessage(EventGridEvent eventGridEvent)
        {
            _logger.LogCritical("🚨 PROCESSWHATSAPPMESSAGE STARTED - EventId: {EventId}", eventGridEvent?.Id ?? "null");

            try
            {
                if (eventGridEvent == null)
                {
                    _logger.LogCritical("❌ EventGridEvent is null.");
                    throw new ArgumentNullException(nameof(eventGridEvent));
                }

                var jsonElement = JsonSerializer.Deserialize<JsonElement>(eventGridEvent.Data.ToString());
                
                // ✅ ADD DETAILED LOGGING FOR DEBUGGING
                _logger.LogCritical("🔍 RAW EVENT DATA: {EventData}", eventGridEvent.Data.ToString());
                _logger.LogCritical("🔍 EVENT TYPE: {EventType}", eventGridEvent.EventType);
                _logger.LogCritical("🔍 EVENT SUBJECT: {Subject}", eventGridEvent.Subject);

                // Log all available properties in the JSON
                _logger.LogCritical("🔍 AVAILABLE PROPERTIES: {Properties}", 
                    string.Join(", ", jsonElement.EnumerateObject().Select(p => $"{p.Name}={p.Value.ValueKind}")));

                var eventData = new WhatsEventType
                {
                    from = GetJsonValue(jsonElement, "from"),
                    to = GetJsonValue(jsonElement, "to"),
                    content = GetJsonValue(jsonElement, "body") ?? GetJsonValue(jsonElement, "content"),
                    channelType = GetJsonValue(jsonElement, "channelType") ?? "whatsapp",
                    receivedTimestamp = eventGridEvent.EventTime.DateTime
                };

                // ✅ ENHANCED MEDIA DETECTION LOGIC
                _logger.LogCritical("🔍 CHECKING FOR MEDIA - Content: {Content}", eventData.content ?? "null");
                
                // Check multiple possible media property patterns
                var mediaFound = false;
                
                // Pattern 1: Direct mediaUri/mediaContentType
                if (jsonElement.TryGetProperty("mediaUri", out var mediaUri) && jsonElement.TryGetProperty("mediaContentType", out var mediaContentType))
                {
                    _logger.LogCritical("📸 MEDIA FOUND (Pattern 1) - URI: {Uri}, Type: {Type}", 
                        mediaUri.GetString(), mediaContentType.GetString());
                    eventData.media = new Media
                    {
                        id = mediaUri.GetString(),
                        mimeType = mediaContentType.GetString(),
                        caption = eventData.content
                    };
                    mediaFound = true;
                }
                // Pattern 2: Attachments array
                else if (jsonElement.TryGetProperty("attachments", out var attachments) && attachments.ValueKind == JsonValueKind.Array && attachments.GetArrayLength() > 0)
                {
                    var firstAttachment = attachments[0];
                    _logger.LogCritical("📎 CHECKING ATTACHMENT - Properties: {Properties}", 
                        string.Join(", ", firstAttachment.EnumerateObject().Select(p => $"{p.Name}={p.Value.ValueKind}")));
                    
                    var attUri = GetJsonValue(firstAttachment, "mediaUri") ?? GetJsonValue(firstAttachment, "url") ?? GetJsonValue(firstAttachment, "uri");
                    var attType = GetJsonValue(firstAttachment, "mediaContentType") ?? GetJsonValue(firstAttachment, "contentType") ?? GetJsonValue(firstAttachment, "mimeType");
                    
                    if (!string.IsNullOrEmpty(attUri) && !string.IsNullOrEmpty(attType))
                    {
                        _logger.LogCritical("📸 MEDIA FOUND (Pattern 2) - URI: {Uri}, Type: {Type}", attUri, attType);
                        eventData.media = new Media
                        {
                            id = attUri,
                            mimeType = attType,
                            caption = eventData.content
                        };
                        mediaFound = true;
                    }
                }
                // Pattern 3: Check for other common WhatsApp media properties
                else if (jsonElement.TryGetProperty("media", out var mediaObj))
                {
                    _logger.LogCritical("📱 CHECKING MEDIA OBJECT - Properties: {Properties}", 
                        string.Join(", ", mediaObj.EnumerateObject().Select(p => $"{p.Name}={p.Value.ValueKind}")));
                    
                    var mediaId = GetJsonValue(mediaObj, "id") ?? GetJsonValue(mediaObj, "mediaId");
                    var mediaMimeType = GetJsonValue(mediaObj, "mimeType") ?? GetJsonValue(mediaObj, "type");
                    
                    if (!string.IsNullOrEmpty(mediaId) && !string.IsNullOrEmpty(mediaMimeType))
                    {
                        _logger.LogCritical("📸 MEDIA FOUND (Pattern 3) - ID: {Id}, Type: {Type}", mediaId, mediaMimeType);
                        eventData.media = new Media
                        {
                            id = mediaId,
                            mimeType = mediaMimeType,
                            caption = eventData.content
                        };
                        mediaFound = true;
                    }
                }

                if (!mediaFound)
                {
                    _logger.LogCritical("❌ NO MEDIA FOUND - This will be processed as text message");
                }

                if (string.IsNullOrEmpty(eventData.from))
                {
                    _logger.LogError("Event data 'from' field is null or empty.");
                    throw new InvalidOperationException("Missing 'from' field in event data.");
                }

                // Log the original phone number from WhatsApp
                _logger.LogInformation("Received WhatsApp message from phone number: {PhoneNumber}", eventData.from);

                // Step 1: Get StoreId from phone number using CatalogStore API
                var storeId = await _sessionService.GetStoreIdByPhoneNumberAsync(eventData.from, _logger);
                if (string.IsNullOrEmpty(storeId))
                {
                    _logger.LogWarning("Could not find store for phone number: {PhoneNumber}. Sending access denied message.", eventData.from);
                    
                    // Send professional message indicating access is not authorized
                    // Use original phone number format for WhatsApp notifications
                    var deniedRecipientList = new List<string> { FormatPhoneNumberForWhatsApp(eventData.from) };
                    var accessDeniedMessage = "🚫 Lo siento, no tienes acceso autorizado a los datos de YOMP. " +
                                            "Para obtener acceso, por favor contacta con tu administrador o equipo de soporte. " +
                                            "📞 Gracias por tu comprensión.";
                    
                    _logger.LogInformation("Sending access denied message to WhatsApp number: {FormattedNumber} (original: {OriginalNumber})", 
                                         deniedRecipientList[0], eventData.from);
                    await _notificationService.SendTextNotification(accessDeniedMessage, deniedRecipientList, _logger);
                    _logger.LogInformation("Access denied message sent to phone number: {PhoneNumber}", eventData.from);
                    return;
                }

                // Step 2: Generate session ID (phone + date)
                var sessionId = Utilities.GenerateSessionId(eventData.from);
                var recipientList = new List<string> { FormatPhoneNumberForWhatsApp(eventData.from) };

                _logger.LogInformation("Processing message for StoreId: {StoreId}, SessionId: {SessionId}, PhoneNumber: {PhoneNumber}, WhatsAppRecipient: {WhatsAppRecipient}", 
                    storeId, sessionId, eventData.from, recipientList[0]);

                // Step 3: Check/Create session (POST /session handles both operations)
                var sessionExists = await _sessionService.CheckSessionExistsAsync(storeId, sessionId, _logger);
                if (!sessionExists)
                {
                    // Session was just created by CheckSessionExistsAsync, no need for additional create call
                    _logger.LogInformation("New session created for SessionId: {SessionId}, StoreId: {StoreId}", sessionId, storeId);
                }
                else
                {
                    _logger.LogInformation("Using existing session for SessionId: {SessionId}, StoreId: {StoreId}", sessionId, storeId);
                }

                if (eventData.media != null)
                {
                    await ProcessMediaMessage(eventData, storeId, sessionId, recipientList);
                }
                else
                {
                    await ProcessTextMessage(eventData, storeId, sessionId, recipientList);
                }

                _logger.LogInformation("WhatsAppWebhook finished processing the event.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while processing the event.");
                throw;
            }
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

        private async Task ProcessMediaMessage(WhatsEventType eventData, string storeId, string sessionId, List<string> recipientList)
        {
            var imageMimeTypes = new HashSet<string> { "image/jpeg", "image/png" };
            // Reuse centralized list from MediaTypes plus commonly encountered WhatsApp types
            var voiceMimeTypes = new HashSet<string>(Whats.Hook.Constants.MediaTypes.VoiceMimeTypes)
            {
                "video/mp4" // keep legacy fallback
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

        private async Task ProcessTextMessage(WhatsEventType eventData, string storeId, string sessionId, List<string> recipientList)
        {
            if (eventData.content != null)
            {
                // Use the new business inquiry endpoint
                var chatCompletion = await _sessionService.ProcessBusinessInquiryAsync(storeId, sessionId, eventData.content, _logger);
                if (!string.IsNullOrEmpty(chatCompletion))
                {
                    await _notificationService.SendTextNotification(chatCompletion, recipientList, _logger);
                }
            }
            else
            {
                _logger.LogError("Event data 'content' field is null.");
            }
        }
    }
}