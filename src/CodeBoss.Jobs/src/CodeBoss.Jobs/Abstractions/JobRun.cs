using System;
using CodeBoss.Jobs.Model;

namespace CodeBoss.Jobs.Abstractions;

/// <summary>
/// Identifies a job row. <paramref name="TenantId"/> is null in single-tenant mode.
/// </summary>
/// <remarks>
/// Replaces the paired tenant/non-tenant overloads that every repository method used to need.
/// </remarks>
public readonly record struct JobRef(int Id, int? TenantId)
{
    public override string ToString() => TenantId.HasValue ? $"job {Id} (tenant {TenantId})" : $"job {Id}";
}

/// <summary>A job execution that has just begun.</summary>
/// <param name="Job">Which job row.</param>
/// <param name="FireInstanceId">Quartz fire id, unique across the cluster. Correlates this with its completion.</param>
/// <param name="SchedulerInstanceId">Which scheduler node is running it.</param>
/// <param name="Attempt">1 for the scheduled fire, 2+ for retries.</param>
/// <param name="StartedUtc">Start time, UTC.</param>
public sealed record JobRunStarted(
    JobRef Job,
    string FireInstanceId,
    string SchedulerInstanceId,
    int Attempt,
    DateTime StartedUtc);

/// <summary>A finished job execution.</summary>
/// <param name="Job">Which job row.</param>
/// <param name="FireInstanceId">Matches the <see cref="JobRunStarted.FireInstanceId"/> of this run.</param>
/// <param name="Attempt">1 for the scheduled fire, 2+ for retries.</param>
/// <param name="Status">Outcome.</param>
/// <param name="Message">Result text on success, exception message on failure. May be empty, never null.</param>
/// <param name="Duration">Wall-clock run time.</param>
/// <param name="CompletedUtc">Completion time, UTC.</param>
/// <param name="Exception">The unwrapped cause when the run failed, otherwise null.</param>
public sealed record JobRunCompleted(
    JobRef Job,
    string FireInstanceId,
    int Attempt,
    JobRunStatus Status,
    string Message,
    TimeSpan Duration,
    DateTime CompletedUtc,
    Exception Exception = null)
{
    /// <summary>True when the job body completed without throwing.</summary>
    public bool IsSuccess => Status == JobRunStatus.Succeeded;
}
