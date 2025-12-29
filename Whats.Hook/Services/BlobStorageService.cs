using System;
using System.IO;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Logging;

namespace Whats.Hook.Services
{
    public class BlobStorageService
    {
        private readonly string _connectionString;
        private readonly string _containerName;
        private BlobContainerClient? _containerClient;

        public BlobStorageService()
        {
            _connectionString = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING")
                ?? throw new InvalidOperationException("AZURE_STORAGE_CONNECTION_STRING is required for audio upload");
            _containerName = Environment.GetEnvironmentVariable("BLOB_CONTAINER_NAME") ?? "whats-audio";
        }

        private async Task<BlobContainerClient> GetContainerAsync(ILogger log)
        {
            if (_containerClient != null) return _containerClient;
            var serviceClient = new BlobServiceClient(_connectionString);
            _containerClient = serviceClient.GetBlobContainerClient(_containerName);
            await _containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob);
            log.LogInformation("Blob container ready: {Container}", _containerName);
            return _containerClient;
        }

        public async Task<Uri?> UploadAudioAsync(byte[] audioBytes, string fileName, string contentType, ILogger log)
        {
            try
            {
                var container = await GetContainerAsync(log);
                var blobClient = container.GetBlobClient(fileName);
                using var ms = new MemoryStream(audioBytes);
                await blobClient.UploadAsync(ms, new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
                });

                // Generate SAS URL valid for 24 hours
                var sasUri = blobClient.GenerateSasUri(BlobSasPermissions.Read, DateTimeOffset.UtcNow.AddHours(24));
                log.LogInformation("Uploaded audio to blob: {Uri}", sasUri);
                return sasUri;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to upload audio to blob storage");
                return null;
            }
        }
    }
}
