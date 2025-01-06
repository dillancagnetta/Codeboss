using System;
using System.Linq;
using System.Threading.Tasks;
using CodeBoss.Extensions;
using CodeBoss.Jobs.Abstractions;
using Quartz;
using Quartz.Impl.Matchers;

namespace CodeBoss.Jobs.Jobs;

public class JobPulse(IServiceJobRepository repository, IServiceJobService service) : CodeBossJob(repository)
{
    
    public override async Task Execute()
    {
        await SynchronizeJobs();
    }

    private async Task SynchronizeJobs()
    {
        int jobsDeleted = 0;
        int jobsScheduleUpdated = 0;
        
        var scheduler = Scheduler;
        var activeJobs = await repository.GetActiveJobsAsync();
        var scheduledQuartzJobs = await scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup());
        
        // delete any jobs that are no longer exist (are not set to active) in the database
        foreach (var jobKey in scheduledQuartzJobs)
        {
            if (!activeJobs.Any(j => j.Guid.ToString() == jobKey.Name))
            {
                await scheduler.DeleteJob(jobKey);
                jobsDeleted++;
            }
        }
        
        // add any jobs that are not yet scheduled
        var newActiveJobs = activeJobs.Where( a => !scheduledQuartzJobs.Any( q => q.Name.AsGuid().Equals( a.Guid ) ) );
        foreach (var job in newActiveJobs)
        {
            const string errorSchedulingStatus = "Error scheduling Job";
            try
            {
                IJobDetail jobDetail = service.BuildQuartzJob( job );
                if ( jobDetail == null )
                {
                    continue;
                }

                ITrigger jobTrigger = service.BuildQuartzTrigger( job );

                // Schedule the job (unless the cron expression is set to never run for an on-demand job like rebuild streaks)
                if ( job.CronExpression != Model.ServiceJob.NeverScheduledCronExpression )
                {
                    scheduler.ScheduleJob( jobDetail, jobTrigger );
                    jobsScheduleUpdated++;
                }

                if ( job.LastStatus == errorSchedulingStatus )
                {
                    job.LastStatusMessage = string.Empty;
                    job.LastStatus = string.Empty;
                    await repository.SaveChangesAsync();
                }
            }
            catch ( Exception ex )
            {
                // create a friendly error message
                string message = string.Format( "Error scheduling the job: {0}.\n\n{2}", job.Name, job.Assembly, ex.Message );
                job.LastStatusMessage = message;
                job.LastStatus = errorSchedulingStatus;
                await repository.SaveChangesAsync();
            }
        }
        
        // reload the jobs in case any where added/removed
        scheduledQuartzJobs = await scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup());
        foreach ( var jobKey in scheduledQuartzJobs )
            {
                var triggersOfJob = await scheduler.GetTriggersOfJob( jobKey );
                var jobCronTrigger = triggersOfJob.OfType<ICronTrigger>().FirstOrDefault();
                var activeJob = activeJobs.FirstOrDefault( a => a.Guid.Equals( jobKey.Name.AsGuid() ) );
                if ( jobCronTrigger == null || activeJob == null )
                {
                    continue;
                }

                bool rescheduleJob = false;

                // fix up the schedule if it has changed
                if ( activeJob.CronExpression != jobCronTrigger.CronExpressionString )
                {
                    rescheduleJob = true;
                }
                else
                {
                    // update the job detail if it has changed
                    IJobDetail scheduledJobDetail = await scheduler.GetJobDetail( jobKey );
                    var activeJobType = activeJob.GetCompiledType();

                    if ( scheduledJobDetail != null && activeJobType != null )
                    {
                        if ( scheduledJobDetail.JobType != activeJobType )
                        {
                            rescheduleJob = true;
                        }
                    }
                }

                if ( rescheduleJob )
                {
                    const string errorReschedulingStatus = "Error re-scheduling Job";
                    try
                    {
                        IJobDetail jobDetail = service.BuildQuartzJob( activeJob );
                        ITrigger newJobTrigger = service.BuildQuartzTrigger( activeJob );
                        bool deletedSuccessfully = await scheduler.DeleteJob( jobKey );
                        await scheduler.RescheduleJob( jobCronTrigger.Key, newJobTrigger );
                        jobsScheduleUpdated++;

                        if ( activeJob.LastStatus == errorReschedulingStatus )
                        {
                            activeJob.LastStatusMessage = string.Empty;
                            activeJob.LastStatus = string.Empty;
                            await repository.SaveChangesAsync();
                        }
                    }
                    catch ( Exception ex )
                    {
                        //ExceptionLogService.LogException( ex, null );

                        // create a friendly error message
                        string message = string.Format( "Error re-scheduling the job: {0}.\n\n{2}", activeJob.Name, activeJob.Assembly, ex.Message );
                        activeJob.LastStatusMessage = message;
                        activeJob.LastStatus = errorReschedulingStatus;
                        await repository.SaveChangesAsync();
                    }
                    
                    Result = string.Empty;

                    if ( jobsDeleted > 0 )
                    {
                        Result += string.Format( "Deleted {0} job schedule(s)", jobsDeleted );
                    }

                    if ( jobsScheduleUpdated > 0 )
                    {
                        Result += ( string.IsNullOrEmpty( this.Result as string ) ? "" : " and " ) + string.Format( "Updated {0} schedule(s)", jobsScheduleUpdated );
                    }
                }
            }
    }
}