using System;
using CodeBoss.Jobs.Model;
using Quartz;

namespace CodeBoss.Jobs.Abstractions;

public interface IServiceJobService
{
    IJobDetail BuildQuartzJob(ServiceJob job);
    ITrigger BuildQuartzTrigger(ServiceJob job);

    ITrigger BuildJobTrigger(ServiceJob job, int? tenantId);
    IJobDetail BuildQuartzJob(ServiceJob job, int? tenantId);

    JobKey GetJobKey(ServiceJob job, int? tenantId);

    /// <summary>
    /// Resolves the CLR type backing a job row, or null when it cannot be found.
    ///
    /// <para>Callers must treat null as a recorded scheduling error rather than skipping silently —
    /// a job that never runs and never explains why is the failure mode this replaced.</para>
    /// </summary>
    /// <remarks>
    /// The default implementation is the legacy <c>Type.GetType(Class, Assembly)</c> lookup, so an
    /// existing custom <see cref="IServiceJobService"/> keeps compiling and behaving as before.
    /// </remarks>
    Type ResolveJobType(ServiceJob job) => job.GetCompiledType();
}