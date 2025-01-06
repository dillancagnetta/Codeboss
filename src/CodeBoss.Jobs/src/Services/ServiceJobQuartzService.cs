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
        var jobKey = new JobKey(job.Guid.ToString(), job.Name);
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
            .WithIdentity( $"{job.Guid}-trigger", job.Name )
            .WithCronSchedule( cronExpression, x =>
            {
                x.InTimeZone( dateTimeProvider.TimeZoneInfo );
                x.WithMisfireHandlingInstructionDoNothing();
            } )
            .StartNow()
            .Build();

        return trigger;
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