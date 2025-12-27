using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Whats.Hook.Models;
using Whats.Hook.Repositories;

namespace Whats.Hook.Services
{
    public class SessionService
    {
        private readonly ChatRepository _chatRepository;
        private readonly CatalogStoreService _catalogStoreService;

        public SessionService(ChatRepository chatRepository, CatalogStoreService catalogStoreService)
        {
            _chatRepository = chatRepository ?? throw new ArgumentNullException(nameof(chatRepository));
            _catalogStoreService = catalogStoreService ?? throw new ArgumentNullException(nameof(catalogStoreService));
        }

        /// <summary>
        /// Process a chat message using the new SRM Chat API.
        /// POST /api/chat with {message, conversation_id, language}
        /// </summary>
        public async Task<string?> ProcessChatAsync(string conversationId, string messageContent, ILogger log, string language = "fr")
        {
            try
            {
                log.LogInformation("Sending chat message to SRM API. ConversationId: {ConversationId}, Language: {Language}", 
                    conversationId, language);

                var response = await _chatRepository.SendChatAsync(messageContent, conversationId, language);
                var responseContent = await response.Content.ReadAsStringAsync();

                log.LogInformation("SRM Chat response status: {StatusCode}", response.StatusCode);
                log.LogDebug("SRM Chat response content: {Response}", responseContent);

                if (response.IsSuccessStatusCode)
                {
                    if (Services.Utilities.IsValidJson(responseContent))
                    {
                        try
                        {
                            // Try to extract the response - adapt based on actual API response format
                            using var doc = JsonDocument.Parse(responseContent);
                            var root = doc.RootElement;

                            // Common response patterns - try each one
                            if (root.TryGetProperty("response", out var responseField))
                            {
                                return responseField.GetString();
                            }
                            if (root.TryGetProperty("message", out var messageField))
                            {
                                return messageField.GetString();
                            }
                            if (root.TryGetProperty("answer", out var answerField))
                            {
                                return answerField.GetString();
                            }
                            if (root.TryGetProperty("whatsapp_summary", out var summaryField))
                            {
                                return summaryField.GetString();
                            }
                            if (root.TryGetProperty("completion", out var completionField))
                            {
                                return completionField.GetString();
                            }

                            // If it's a simple string response
                            if (root.ValueKind == JsonValueKind.String)
                            {
                                return root.GetString();
                            }

                            log.LogWarning("Could not find response field in API response. Available properties: {Properties}",
                                string.Join(", ", root.EnumerateObject().Select(p => p.Name)));
                        }
                        catch (JsonException ex)
                        {
                            log.LogError(ex, "Failed to deserialize chat response.");
                        }
                    }
                    else
                    {
                        // If it's not JSON, return the raw response
                        return responseContent;
                    }
                }
                else
                {
                    log.LogError("Chat request failed. StatusCode: {StatusCode}, Response: {Response}", 
                        response.StatusCode, responseContent);
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex, "An error occurred while processing the chat.");
                throw;
            }

            return null;
        }

        #region Deprecated - Kept for potential future use

        [Obsolete("Phone-to-store resolution removed. Use conversation_id (GUID) instead.")]
        public async Task<string?> GetStoreIdByPhoneNumberAsync(string phoneNumber, ILogger log)
        {
            try
            {
                log.LogInformation("Looking up store for phone number: {PhoneNumber}", phoneNumber);
                
                var storeInfo = await _catalogStoreService.GetStoreByPhoneNumberAsync(phoneNumber);
                if (storeInfo != null && storeInfo.StoreId > 0)
                {
                    var storeIdString = storeInfo.StoreId.ToString();
                    log.LogInformation("Found store ID: {StoreId} for phone: {PhoneNumber}", storeIdString, phoneNumber);
                    return storeIdString;
                }
                else
                {
                    log.LogWarning("No store found for phone number: {PhoneNumber}", phoneNumber);
                    return null;
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error getting store ID for phone: {PhoneNumber}", phoneNumber);
                return null;
            }
        }

        [Obsolete("Session management removed. Use conversation_id (GUID) instead.")]
        public async Task<bool> CheckSessionExistsAsync(string storeId, string sessionId, ILogger log)
        {
            try
            {
                var sessionRequest = new SessionCreateRequest
                {
                    session_id = sessionId,
                    store_id = storeId
                };

                var response = await _chatRepository.CreateSessionAsync(sessionRequest);
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var sessionResponse = JsonSerializer.Deserialize<SessionResponse>(responseContent, new JsonSerializerOptions 
                    { 
                        PropertyNameCaseInsensitive = true 
                    });

                    if (sessionResponse != null)
                    {
                        bool sessionExisted = !(sessionResponse.created_new ?? false);
                        log.LogInformation("Session check result: SessionId={SessionId}, StoreId={StoreId}, Existed={Existed}, Message={Message}", 
                            sessionId, storeId, sessionExisted, sessionResponse.message ?? "No message");
                        return sessionExisted;
                    }
                }
                else
                {
                    log.LogWarning("Session check failed. StatusCode: {StatusCode}", response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex, "An error occurred while checking the session. SessionId: {SessionId}, StoreId: {StoreId}", sessionId, storeId);
                throw;
            }

            return false;
        }

        [Obsolete("Session management removed. Use conversation_id (GUID) instead.")]
        public async Task CreateSessionAsync(string storeId, string sessionId, ILogger log)
        {
            await Task.CompletedTask;
            log.LogInformation("CreateSessionAsync called - session should already exist from CheckSessionExistsAsync. SessionId: {SessionId}, StoreId: {StoreId}", sessionId, storeId);
        }

        [Obsolete("Use ProcessChatAsync instead. Kept for backward compatibility.")]
        public async Task<string?> ProcessBusinessInquiryAsync(string storeId, string sessionId, string messageContent, ILogger log)
        {
            try
            {
                var request = new BusinessInquiryRequest
                {
                    inquiry = messageContent,
                    store_id = storeId,
                    session_id = sessionId,
                    business_context = "WhatsApp message"
                };

                var response = await _chatRepository.SendBusinessInquiryAsync(request);
                var responseContent = await response.Content.ReadAsStringAsync();

                log.LogInformation("Business inquiry response: {Response}", responseContent);

                if (response.IsSuccessStatusCode)
                {
                    if (Services.Utilities.IsValidJson(responseContent))
                    {
                        try
                        {
                            var inquiryResponse = JsonSerializer.Deserialize<BusinessInquiryResponse>(responseContent, new JsonSerializerOptions 
                            { 
                                PropertyNameCaseInsensitive = true 
                            });

                            if (inquiryResponse != null && !string.IsNullOrEmpty(inquiryResponse.whatsapp_summary))
                            {
                                return inquiryResponse.whatsapp_summary;
                            }
                            else
                            {
                                log.LogError("Business inquiry response is null or empty summary.");
                            }
                        }
                        catch (JsonException ex)
                        {
                            log.LogError(ex, "Failed to deserialize business inquiry response.");
                        }
                    }
                    else
                    {
                        log.LogError("Business inquiry response is not valid JSON.");
                    }
                }
                else
                {
                    log.LogError("Business inquiry failed. StatusCode: {StatusCode}, Response: {Response}", response.StatusCode, responseContent);
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex, "An error occurred while processing the business inquiry.");
                throw;
            }

            return null;
        }

        #endregion
    }
}
