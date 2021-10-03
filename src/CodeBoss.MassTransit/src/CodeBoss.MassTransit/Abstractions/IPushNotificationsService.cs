using System.Threading.Tasks;

namespace CodeBoss.MassTransit.Services
{
    public interface IPushNotificationsService<in TNotification, in TPublisher>
    {
        Task PushAsync(TNotification notification, TPublisher publisher);
    }
}
