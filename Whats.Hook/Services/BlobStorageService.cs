using System;
using System.IO;
using System.Threading.Tasks;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Logging;

namespace Whats.Hook.Services
{
    public class BlobStorageService
    {
        private readonly string _storageAccountName;
        private readonly string _containerName;
        private readonly string? _userAssignedClientId;
        private BlobContainerClient? _containerClient;
        private BlobServiceClient? _serviceClient;

        public BlobStorageService()
        {
            // Storage account name (without full URL)
            _storageAccountName = Environment.GetEnvironmentVariable("AZURE_STORAGE_ACCOUNT_NAME") ?? "stg4whatsaudior";
            _containerName = Environment.GetEnvironmentVariable("BLOB_CONTAINER_NAME") ?? "whats-audio";
            // Optional: User-assigned managed identity client ID
            _userAssignedClientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");
        }

        private BlobServiceClient GetServiceClient(ILogger log)
        {
            if (_serviceClient != null) return _serviceClient;

            var blobUri = new Uri($"https://{_storageAccountName}.blob.core.windows.net");
            
            // Use DefaultAzureCredential with optional user-assigned identity
            DefaultAzureCredential credential;
            if (!string.IsNullOrEmpty(_userAssignedClientId))
            {
                log.LogInformation("Using user-assigned managed identity: {ClientId}", _userAssignedClientId);
                credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
                {
                    ManagedIdentityClientId = _userAssignedClientId
                });
            }
            else
            {
                log.LogInformation("Using system-assigned managed identity or default credentials");
                credential = new DefaultAzureCredential();
            }

            _serviceClient = new BlobServiceClient(blobUri, credential);
            return _serviceClient;
        }

        private async Task<BlobContainerClient> GetContainerAsync(ILogger log)
        {
            if (_containerClient != null) return _containerClient;
            
            var serviceClient = GetServiceClient(log);
            _containerClient = serviceClient.GetBlobContainerClient(_containerName);
            
            // Container should already exist - just verify access
            log.LogInformation("Using blob container: {Container}", _containerName);
            return _containerClient;
        }

        public async Task<Uri?> UploadAudioAsync(byte[] audioBytes, string fileName, string contentType, ILogger log)
        {
            try
            {
                var container = await GetContainerAsync(log);
                var blobClient = container.GetBlobClient(fileName);
                
                log.LogInformation("Uploading audio {Size} bytes to {Blob}", audioBytes.Length, fileName);
                
                using var ms = new MemoryStream(audioBytes);
                await blobClient.UploadAsync(ms, new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
                });

                // Use User Delegation SAS for Managed Identity (more secure)
                var userDelegationKey = await GetServiceClient(log).GetUserDelegationKeyAsync(
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow.AddHours(24));

                var sasBuilder = new BlobSasBuilder
                {
                    BlobContainerName = _containerName,
                    BlobName = fileName,
                    Resource = "b", // blob
                    StartsOn = DateTimeOffset.UtcNow.AddMinutes(-5),
                    ExpiresOn = DateTimeOffset.UtcNow.AddHours(24)
                };
                sasBuilder.SetPermissions(BlobSasPermissions.Read);

                var sasUri = new BlobUriBuilder(blobClient.Uri)
                {
                    Sas = sasBuilder.ToSasQueryParameters(userDelegationKey, _storageAccountName)
                };

                log.LogInformation("✅ Uploaded audio to blob: {Uri}", sasUri.ToUri());
                return sasUri.ToUri();
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to upload audio to blob storage");
                return null;
            }
        }
    }
}
