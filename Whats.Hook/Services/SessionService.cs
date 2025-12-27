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

        public async Task<bool> CheckSessionExistsAsync(string storeId, string sessionId, ILogger log)
        {
            try
            {
                // Use the POST /session endpoint which returns existing sessions
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
                        // Check if it was an existing session or newly created
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

        public async Task CreateSessionAsync(string storeId, string sessionId, ILogger log)
        {
            // This method is now redundant since CheckSessionExistsAsync already creates the session
            // But we'll keep it for backward compatibility and just log that the session should already exist
            await Task.CompletedTask; // Make it truly async
            log.LogInformation("CreateSessionAsync called - session should already exist from CheckSessionExistsAsync. SessionId: {SessionId}, StoreId: {StoreId}", sessionId, storeId);
        }

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

        public async Task<string?> ProcessChatAsync(string storeId, string sessionId, string messageContent, ILogger log)
        {
            var businessInquiryRequest = new BusinessInquiryRequest
            {
                session_id = sessionId,
                store_id = storeId,
                inquiry = messageContent
            };

            try
            {
                var chatResponse = await _chatRepository.SendBusinessInquiryAsync(businessInquiryRequest);
                var chatResult = await chatResponse.Content.ReadAsStringAsync();

                log.LogInformation("Chat response: {ChatResult}", chatResult);

                if (string.IsNullOrEmpty(chatResult))
                {
                    log.LogError("Chat result is null or empty.");
                    return null;
                }

                if (chatResult == "Invalid request")
                {
                    log.LogError("Chat response indicates an invalid request.");
                    return null;
                }

                if (Services.Utilities.IsValidJson(chatResult))
                {
                    try
                    {
                        // Try to deserialize as BusinessInquiryResponse first
                        var businessResponse = JsonSerializer.Deserialize<BusinessInquiryResponse>(chatResult);
                        if (businessResponse != null && !string.IsNullOrEmpty(businessResponse.whatsapp_summary))
                        {
                            return businessResponse.whatsapp_summary;
                        }

                        // Fallback to the old ChatCompletion format for backward compatibility
                        var chatCompletionObject = JsonSerializer.Deserialize<ChatCompletion>(chatResult);
                        if (chatCompletionObject != null && !string.IsNullOrEmpty(chatCompletionObject.completion))
                        {
                            return chatCompletionObject.completion;
                        }
                        else
                        {
                            log.LogError("Chat completion object is null or empty.");
                        }
                    }
                    catch (JsonException ex)
                    {
                        log.LogError(ex, "Failed to deserialize chat result.");
                    }
                }
                else
                {
                    log.LogError("Chat result is not a valid JSON.");
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex, "An error occurred while processing the chat.");
                throw;
            }

            return null;
        }
    }
}
