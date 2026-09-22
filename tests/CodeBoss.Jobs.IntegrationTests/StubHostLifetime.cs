using Microsoft.Extensions.Hosting;

namespace CodeBoss.Jobs.IntegrationTests;

/// <summary>
/// Stands in for the generic host's lifetime so <c>AddQuartzHostedService</c>'s registration can be
/// constructed. These tests start the scheduler by hand, so the hosted service never runs; it only
/// has to resolve, because the nodes are built with <c>ValidateOnBuild</c> on — which is the point,
/// since that is what proves every job's dependencies resolve at startup.
/// </summary>
public sealed class StubHostLifetime : IHostApplicationLifetime
{
    private readonly CancellationTokenSource _stopping = new();

    public CancellationToken ApplicationStarted { get; } = new CancellationTokenSource().Token;
    public CancellationToken ApplicationStopping => _stopping.Token;
    public CancellationToken ApplicationStopped { get; } = new CancellationTokenSource().Token;

    public void StopApplication() => _stopping.Cancel();
}
