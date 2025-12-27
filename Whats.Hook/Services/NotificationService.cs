using Azure.Communication.Messages;
using Microsoft.Extensions.Logging;

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
    }
}
