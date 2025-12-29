using Azure.Communication.Messages;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Whats.Hook.Services
{
    public class NotificationService
    {
        private static readonly string connectionString = Environment.GetEnvironmentVariable("COMMUNICATION_SERVICES_CONNECTION_STRING") 
            ?? throw new InvalidOperationException("COMMUNICATION_SERVICES_CONNECTION_STRING environment variable is required");
        private static readonly string channelRegId = Environment.GetEnvironmentVariable("CHANNEL_REGISTRATION_ID") 
            ?? throw new InvalidOperationException("CHANNEL_REGISTRATION_ID environment variable is required");

        public async Task SendTextNotification(string chatCompletion, List<string> recipientList, ILogger log)
        {
            var notificationMessagesClient = new NotificationMessagesClient(connectionString);
            var textContent = new TextNotificationContent(new Guid(channelRegId), recipientList, chatCompletion);
            var sendTextMessageResult = await notificationMessagesClient.SendAsync(textContent);
            log.LogInformation($"Text notification sent with result: {sendTextMessageResult.GetRawResponse().Status}");
        }

        /// <summary>
        /// Send audio message via WhatsApp using AudioNotificationContent (recommended API).
        /// </summary>
        public async Task SendAudioNotificationFromUrl(Uri audioUrl, List<string> recipientList, ILogger log)
        {
            var notificationMessagesClient = new NotificationMessagesClient(connectionString);
            var audioContent = new AudioNotificationContent(new Guid(channelRegId), recipientList, audioUrl);
            var result = await notificationMessagesClient.SendAsync(audioContent);
            log.LogInformation($"🔊 Audio notification sent with result: {result.GetRawResponse().Status}");
        }

        /// <summary>
        /// Send image message via WhatsApp using ImageNotificationContent.
        /// </summary>
        public async Task SendImageNotificationFromUrl(Uri imageUrl, List<string> recipientList, string? caption, ILogger log)
        {
            var notificationMessagesClient = new NotificationMessagesClient(connectionString);
            var imageContent = new ImageNotificationContent(new Guid(channelRegId), recipientList, imageUrl)
            {
                Caption = caption
            };
            var result = await notificationMessagesClient.SendAsync(imageContent);
            log.LogInformation($"📸 Image notification sent with result: {result.GetRawResponse().Status}");
        }

        /// <summary>
        /// Send document message via WhatsApp using DocumentNotificationContent.
        /// </summary>
        public async Task SendDocumentNotificationFromUrl(Uri documentUrl, List<string> recipientList, string fileName, string? caption, ILogger log)
        {
            var notificationMessagesClient = new NotificationMessagesClient(connectionString);
            var documentContent = new DocumentNotificationContent(new Guid(channelRegId), recipientList, documentUrl)
            {
                FileName = fileName,
                Caption = caption
            };
            var result = await notificationMessagesClient.SendAsync(documentContent);
            log.LogInformation($"📄 Document notification sent with result: {result.GetRawResponse().Status}");
        }

        /// <summary>
        /// Send video message via WhatsApp using VideoNotificationContent.
        /// </summary>
        public async Task SendVideoNotificationFromUrl(Uri videoUrl, List<string> recipientList, string? caption, ILogger log)
        {
            var notificationMessagesClient = new NotificationMessagesClient(connectionString);
            var videoContent = new VideoNotificationContent(new Guid(channelRegId), recipientList, videoUrl)
            {
                Caption = caption
            };
            var result = await notificationMessagesClient.SendAsync(videoContent);
            log.LogInformation($"🎥 Video notification sent with result: {result.GetRawResponse().Status}");
        }

        /// <summary>
        /// Upload audio bytes to blob storage and send as WhatsApp audio message.
        /// </summary>
        public async Task SendAudioNotificationFromBytes(byte[] audioBytes, string fileName, string contentType, BlobStorageService blobService, List<string> recipientList, ILogger log)
        {
            var url = await blobService.UploadAudioAsync(audioBytes, fileName, contentType, log);
            if (url != null)
            {
                await SendAudioNotificationFromUrl(url, recipientList, log);
            }
            else
            {
                log.LogWarning("Failed to upload audio; falling back to text notification");
                await SendTextNotification("[Audio unavailable]", recipientList, log);
            }
        }
    }
}
