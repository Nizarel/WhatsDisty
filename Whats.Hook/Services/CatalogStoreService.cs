using System.Text.Json;
using Microsoft.Extensions.Logging;
using Whats.Hook.Models;

namespace Whats.Hook.Services
{
    public class CatalogStoreService
    {
        private readonly HttpClient _httpClient;
        private readonly string _catalogStoreApiUrl;
        private readonly ILogger<CatalogStoreService> _logger;

        public CatalogStoreService(HttpClient httpClient, ILogger<CatalogStoreService> logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            _catalogStoreApiUrl = Environment.GetEnvironmentVariable("CATALOG_STORE_API_URL")
                ?? "https://catalogstore-api.thankfuldune-81948d3c.eastus2.azurecontainerapps.io";
        }

        public async Task<StoreInfo?> GetStoreByPhoneNumberAsync(string phoneNumber)
        {
            try
            {
                var sanitizedPhone = SanitizePhoneNumber(phoneNumber);
                
                // Try the sanitized phone number first
                var storeInfo = await TryGetStoreByPhoneAsync(sanitizedPhone);
                if (storeInfo != null)
                {
                    return storeInfo;
                }

                // If not found and we modified the number, try some fallback strategies
                if (sanitizedPhone != phoneNumber)
                {
                    _logger.LogInformation("Store not found with sanitized number {SanitizedPhone}, trying fallback strategies for {OriginalPhone}", 
                        sanitizedPhone, phoneNumber);

                    // Try with just digits (no country code removal)
                    var digitsOnly = new string(phoneNumber.Where(char.IsDigit).ToArray());
                    if (digitsOnly != sanitizedPhone)
                    {
                        storeInfo = await TryGetStoreByPhoneAsync(digitsOnly);
                        if (storeInfo != null)
                        {
                            _logger.LogInformation("Found store using digits-only fallback: {DigitsOnly}", digitsOnly);
                            return storeInfo;
                        }
                    }

                    // For US numbers, try with leading 1 if we removed it
                    if (sanitizedPhone.Length == 10)
                    {
                        var withCountryCode = "1" + sanitizedPhone;
                        storeInfo = await TryGetStoreByPhoneAsync(withCountryCode);
                        if (storeInfo != null)
                        {
                            _logger.LogInformation("Found store using US country code format: {WithCountryCode}", withCountryCode);
                            return storeInfo;
                        }
                    }
                }

                _logger.LogWarning("Store not found for phone number: {OriginalPhone} (tried: {SanitizedPhone})", phoneNumber, sanitizedPhone);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting store info for phone: {PhoneNumber}", phoneNumber);
                return null;
            }
        }

        private async Task<StoreInfo?> TryGetStoreByPhoneAsync(string phoneNumber)
        {
            try
            {
                var url = $"{_catalogStoreApiUrl}/api/catalogstore/by-mobile/{phoneNumber}";
                
                _logger.LogDebug("Trying to get store info for phone: {PhoneNumber} from URL: {Url}", phoneNumber, url);

                var response = await _httpClient.GetAsync(url);
                
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var storeInfo = JsonSerializer.Deserialize<StoreInfo>(content, new JsonSerializerOptions 
                    { 
                        PropertyNameCaseInsensitive = true 
                    });
                    
                    _logger.LogInformation("Found store: {StoreId} for phone: {PhoneNumber}", storeInfo?.StoreId, phoneNumber);
                    return storeInfo;
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogDebug("Store not found for phone number: {PhoneNumber}", phoneNumber);
                    return null;
                }
                else
                {
                    _logger.LogWarning("Failed to get store info for {PhoneNumber}. StatusCode: {StatusCode}", phoneNumber, response.StatusCode);
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during store lookup for phone: {PhoneNumber}", phoneNumber);
                return null;
            }
        }

        private string SanitizePhoneNumber(string phoneNumber)
        {
            if (string.IsNullOrEmpty(phoneNumber))
                return string.Empty;

            // Remove all non-digit characters
            var digitsOnly = new string(phoneNumber.Where(char.IsDigit).ToArray());
            
            _logger.LogDebug("Phone digits only: {OriginalPhone} -> {DigitsOnly} (length: {Length})", phoneNumber, digitsOnly, digitsOnly.Length);
            
            // Handle country codes - remove common prefixes
            // Mexico: +52 (remove leading 52 if starts with 52)
            // US/Canada: +1 (remove leading 1 if 11 digits)
            
            // Check Mexico first (more specific patterns)
            if (digitsOnly.Length == 13 && digitsOnly.StartsWith("521"))
            {
                // Mexico mobile format: +521 followed by 10 digits
                digitsOnly = digitsOnly.Substring(3);
                _logger.LogDebug("Removed Mexico mobile country code (+521). Phone: {OriginalPhone} -> {SanitizedPhone}", phoneNumber, digitsOnly);
            }
            else if (digitsOnly.Length == 12 && digitsOnly.StartsWith("52"))
            {
                // Mexico landline format: +52 followed by 10 digits  
                digitsOnly = digitsOnly.Substring(2);
                _logger.LogDebug("Removed Mexico country code (+52). Phone: {OriginalPhone} -> {SanitizedPhone}", phoneNumber, digitsOnly);
            }
            else if (digitsOnly.Length == 11 && digitsOnly.StartsWith("1"))
            {
                // US/Canada format: +1 followed by 10 digits
                digitsOnly = digitsOnly.Substring(1);
                _logger.LogDebug("Removed US/Canada country code (+1). Phone: {OriginalPhone} -> {SanitizedPhone}", phoneNumber, digitsOnly);
            }
            
            _logger.LogDebug("Final sanitized phone number: {OriginalPhone} -> {SanitizedPhone}", phoneNumber, digitsOnly);
            return digitsOnly;
        }
    }
}
