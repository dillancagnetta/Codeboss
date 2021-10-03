namespace Codeboss.Types
{
    public class Constants
    {

        public class Notifications
        {
            public class Scope
            {
                public const string User = "USER";
                public const string All = "ALL";
            }

            public class Type
            {
                public const string Success = "success";
                public const string Warning = "warning";
                public const string Information = "info";
                public const string Danger = "danger";
            }

            public class Method
            {
                public const string Alert = "alertNotification";
                public const string Direct = "directMessage";
                public const string Broadcast = "broadcastMessage";
            }
        }
    }
}
