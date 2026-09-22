using System;
using CodeBoss.Extensions;
using CodeBoss.Jobs.Jobs;
using CodeBoss.Jobs.Model;
using CodeBoss.Jobs.Services;
using Quartz;

namespace CodeBoss.Jobs;

public static class QuartzExtensionMethods
{
    /// <summary>
    /// <c>JobDataMap</c> keys the library writes onto triggers rather than job details.
    /// </summary>
    public static class JobDataKeys
    {
        /// <summary>Retry attempt counter. 1 (or absent) is the scheduled fire.</summary>
        public const string Attempt = "Attempt";
    }

    public static int GetJobIdFromQuartz( this IJobExecutionContext context )
    {
        return context.JobDetail.Description.AsInteger();
    }

    public static Type GetCompiledType(this ServiceJob job)
    {
        var jobType = Type.GetType( $"{job.Class}, {job.Assembly}", false, true );
        return jobType;
    }

    public static int? GetTenantIdFromQuartz(this IJobExecutionContext context)
    {
        if (context.JobDetail.JobDataMap.ContainsKey(ServiceJobQuartzService.TenantIdKey))
        {
            return context.JobDetail.JobDataMap.GetIntValue(ServiceJobQuartzService.TenantIdKey);
        }
        return null;
    }

    /// <summary>
    /// The job row this fire belongs to, or null for a System job such as the pulse, which has no
    /// <c>ServiceJob</c> behind it.
    /// </summary>
    public static Abstractions.JobRef? GetJobRef(this IJobExecutionContext context)
    {
        if (context.JobDetail.Key.Group == JobGroups.System) return null;

        return new Abstractions.JobRef(context.GetJobIdFromQuartz(), context.GetTenantIdFromQuartz());
    }

    /// <summary>
    /// Which attempt this fire is: 1 for the scheduled fire, 2 and above for retries.
    /// </summary>
    /// <remarks>
    /// Read from the MERGED data map, so the counter can ride on a one-shot retry trigger without
    /// mutating the stored job detail.
    /// </remarks>
    public static int GetAttempt(this IJobExecutionContext context)
    {
        var map = context?.MergedJobDataMap;

        // JobDataMap.GetString THROWS KeyNotFoundException for an absent key — it does not return
        // null. Guard with ContainsKey; the scheduled fire has no Attempt entry at all.
        if (map is null || !map.ContainsKey(JobDataKeys.Attempt)) return 1;

        return int.TryParse(map.GetString(JobDataKeys.Attempt), out var attempt) && attempt > 0
            ? attempt
            : 1;
    }

    /// <summary>
    /// Drills through the wrappers Quartz puts around a job's real failure: nested
    /// <see cref="SchedulerException"/>s, and an <see cref="AggregateException"/> holding exactly
    /// one inner exception. Returns null for a null input.
    /// </summary>
    public static Exception Unwrap(this Exception exception)
    {
        var current = exception;

        while (current is SchedulerException && current.InnerException is not null)
        {
            current = current.InnerException;
        }

        if (current is AggregateException { InnerExceptions.Count: 1 } aggregate)
        {
            current = aggregate.InnerExceptions[0];
        }

        return current;
    }

    public static bool IsValidCronDescription( this string cronExpression )
    {
        return CronExpression.IsValidExpression( cronExpression );
    }
}
