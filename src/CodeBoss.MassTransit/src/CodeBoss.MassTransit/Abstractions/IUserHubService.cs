using System.Threading.Tasks;
using MassTransit;

namespace CodeBoss.MassTransit.Abstractions
{
    public interface IUserHubService
    {
        Task SendToUserAsync<TModel>(TModel model, string userId, string methodName, IPublishEndpoint publisher);
    }
}