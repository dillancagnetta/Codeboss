using System.Threading;
using System.Threading.Tasks;
using Quartz;

namespace CodeBoss.Jobs.Abstractions;

/// <summary>
/// Reacts to a finished job run, after the library has already persisted its status and history.
///
/// <para>This is the extension point for consumer-specific behaviour that used to force a whole
/// custom <see cref="ICodeBossJobListener"/> — sending notification emails, raising domain events,
/// pushing metrics. Register as many as needed; all are invoked.</para>
///
/// <para>Observers are invoked in sequence and are isolated: one that throws is logged and does not
/// prevent the others from running, nor does it fail the job, which has already completed by this
/// point.</para>
/// </summary>
public interface IJobRunObserver
{
    Task OnCompletedAsync(IJobExecutionContext context, JobRunCompleted run, CancellationToken ct = default);
}
