using System.Threading.Tasks;

namespace CodeBoss.MassTransit.Services
{
    public interface IHubTransportService<in TPublisher>
    {
        Task SendToUserAsync<TModel>(TModel model, string userId, string methodName, TPublisher publisher);
    }
}
