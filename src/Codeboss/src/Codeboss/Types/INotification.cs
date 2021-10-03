using System;

namespace Codeboss.Types
{
    public interface INotification : IHaveUserId<string>, IHaveCorrelationId<Guid>
    {
        string Scope { get; set; } // ALL or User
        string Type { get; set; } // warn, success, error
        string Title { get; set; } // Title
        string Payload { get; set; }
        string MethodName { get; set; }
        bool Read { get; set; }
        string Icon { get; set; } // Icon to display
    }
}
