using System;
using CodeBoss.Jobs.Abstractions;
using CodeBoss.Jobs.Model;
using Codeboss.Types;
using Quartz;

namespace CodeBoss.Jobs.Services;

public class ServiceJobQuartzService(IDateTimeProvider dateTimeProvider) : IServiceJobService
{
    public IJobDetail BuildQuartzJob(ServiceJob job)
    {
        var jobKey = new JobKey(job.JobKey.ToString(), job.Name);
        var jobType = job.GetCompiledType();
        if ( jobType == null )
        {
            return null;
        }

        var map = job.JobParameters != null 
            ? new JobDataMap(job.JobParameters) 
            : new JobDataMap();
        
        var jobDetail = JobBuilder.Create(jobType)
            .WithIdentity(jobKey)
            .WithDescription( job.Id.ToString() )
            .UsingJobData( map )
            .Build();
        
        return jobDetail;
    }

    public ITrigger BuildQuartzTrigger(ServiceJob job)
    {
        string cronExpression;
        if ( IsValidCronDescription( job.CronExpression ) )
        {
            cronExpression = job.CronExpression;
        }
        else
        {
            // Invalid cron expression, so specify to never run.
            // If they view the job in ScheduledJobDetail they'll see that it isn't a valid expression.
            cronExpression = ServiceJob.NeverScheduledCronExpression;
        }
        
        // create quartz trigger
        ITrigger trigger = ( ICronTrigger ) TriggerBuilder.Create()
            .WithIdentity( $"{job.JobKey}-trigger", job.Name )
            .WithCronSchedule( cronExpression, x =>
            {
                x.InTimeZone( dateTimeProvider?.TimeZoneInfo ?? TimeZoneInfo.Utc );
                x.WithMisfireHandlingInstructionDoNothing();
            } )
            .StartNow()
            .Build();

        return trigger;
    }
    
    public ITrigger BuildJobTrigger(ServiceJob job, int? tenantId)
    {
        var jobKey = GetJobKey(job, tenantId);
        var triggerKey = new TriggerKey($"{jobKey.Name}_trigger", jobKey.Group);
        
        string cronExpression = IsValidCronDescription(job.CronExpression) 
            ? job.CronExpression 
            : ServiceJob.NeverScheduledCronExpression;

        return TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .WithCronSchedule(cronExpression, x =>
            {
                x.InTimeZone(TimeZoneInfo.Utc);
                x.WithMisfireHandlingInstructionDoNothing();
            })
            .StartNow()
            .Build();
    }

    public IJobDetail BuildQuartzJob(ServiceJob job, int? tenantId)
    {
        var jobKey = GetJobKey(job, tenantId);
        var jobType = job.GetCompiledType();
        if (jobType == null) return null;

        var map = job.JobParameters != null ? new JobDataMap(job.JobParameters) : new JobDataMap();
        
        // Add tenant ID if this is a tenant job
        if (tenantId.HasValue)
        {
            map.Put("TenantId", tenantId.Value);
        }

        return JobBuilder.Create(jobType)
            .WithIdentity(jobKey)
            .WithDescription(job.Id.ToString())
            .UsingJobData(map)
            .Build();
    }
    
    public JobKey GetJobKey(ServiceJob job, int? tenantId)
    {
        if (tenantId.HasValue)
        {
            return new JobKey($"{job.JobKey}_{tenantId}", $"tenant_{tenantId}");
        }
        else
        {
            return new JobKey(job.JobKey.ToString(), "default");
        }
    }

    /// <summary>
    /// Determines whether the Cron Expression is valid for Quartz
    /// </summary>
    /// <param name="cronExpression">The cron expression.</param>
    /// <returns>bool.</returns>
    public static bool IsValidCronDescription( string cronExpression )
    {
        return Quartz.CronExpression.IsValidExpression( cronExpression );
    }
}