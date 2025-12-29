using Azure.Communication.Messages;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

//#pragma warning disable CS0618 // MediaNotificationContent is deprecated but needed for audio until ACS provides alternative

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

        public async Task SendAudioNotificationFromUrl(Uri audioUrl, List<string> recipientList, ILogger log)
        {
            var notificationMessagesClient = new NotificationMessagesClient(connectionString);
            var mediaContent = new MediaNotificationContent(new Guid(channelRegId), recipientList, audioUrl);
            var result = await notificationMessagesClient.SendAsync(mediaContent);
            log.LogInformation($"Audio notification sent with result: {result.GetRawResponse().Status}");
        }

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

//#pragma warning restore CS0618
