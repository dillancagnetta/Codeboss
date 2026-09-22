using System;
using Quartz;

namespace CodeBoss.Jobs.Jobs;

/// <summary>
/// A job exceeded its <c>ServiceJob.TimeoutSeconds</c>.
///
/// <para>Distinct from a bare <see cref="OperationCanceledException"/> on purpose: the scheduler
/// cancels running jobs during shutdown too, and "we shut the host down" must not be recorded as
/// "this job is too slow".</para>
/// </summary>
public sealed class JobTimeoutException(string jobName, TimeSpan timeout)
    : JobExecutionException($"Job '{jobName}' exceeded its timeout of {timeout}.")
{
    public string JobName { get; } = jobName;
    public TimeSpan Timeout { get; } = timeout;
}
