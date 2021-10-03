using System;

namespace Codeboss.Types
{
    public class Notification : INotification
    {
        public string Scope { get; set; } // ALL or User
        public string Type { get; set; }
        public string Title { get; set; }
        public string Payload { get; set; }
        public string MethodName { get; set; }
        public bool Read { get; set; }
        public string Icon { get; set; } // Icon to display
        public string UserId { get; set; }

        public static INotification UserNotification(string type, string title, string payload, string methodName, string userId) => new Notification
        {
            Scope = Constants.Notifications.Scope.User,
            Type = type,
            Title = title,
            Payload = payload,
            MethodName = methodName,
            UserId = userId
        };

        public Guid CorrelationId { get; set; }
    }
}
