using System.Collections.Generic;
using System.Threading.Tasks;
using CodeBoss.MassTransit.Abstractions;
using MassTransit;
using MassTransit.SignalR.Contracts;
using MassTransit.SignalR.Utils;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Protocol;

namespace CodeBoss.MassTransit.Services
{
    public abstract class MassTransitSignalRHubService<THub> :
        IHubTransportService<IPublishEndpoint>,
        IUserHubService
        where THub : Hub
    {
        private readonly IReadOnlyList<IHubProtocol> _protocols = new IHubProtocol[] {new JsonHubProtocol()};

        public async Task SendToUserAsync<TModel>(TModel model, string userId, string methodName, IPublishEndpoint publisher)
        {
            await publisher.Publish<User<THub>>(new
            {
                UserId = userId,
                Messages = _protocols.ToProtocolDictionary(methodName, new object[] {model})
            }).ConfigureAwait(false);
        }
    }
}