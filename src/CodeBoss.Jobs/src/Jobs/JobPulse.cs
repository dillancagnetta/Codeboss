﻿using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeBoss.Extensions;
using CodeBoss.Jobs.Abstractions;
using CodeBoss.Jobs.Model;
using Microsoft.Extensions.Logging;
using Quartz;
using Quartz.Impl.Matchers;

namespace CodeBoss.Jobs.Jobs;

public class JobPulse(
    IServiceJobRepository repository, 
    IServiceJobService service,
    ILogger<CodeBossJob> logger) : CodeBossJob(repository, logger)
{
    public override async Task Execute(CancellationToken ct = default)
    {
        await SynchronizeJobs(ct);
    }

    private async Task SynchronizeJobs(CancellationToken ct)
    {
        int jobsDeleted = 0;
        int jobsScheduleUpdated = 0;

        var scheduler = Scheduler;
        var activeJobs = await repository.GetActiveJobsAsync(ct);
        var scheduledQuartzJobs = (await scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup(), ct))
            .Where(jobKey => jobKey.Group != "System").ToList();

        // delete any jobs that are no longer exist (are not set to active) in the database
        foreach (var jobKey in scheduledQuartzJobs)
        {
            if (!activeJobs.Any(j => j.Guid.ToString() == jobKey.Name))
            {
                await scheduler.DeleteJob(jobKey, ct);
                jobsDeleted++;
            }
        }

        // add any jobs that are not yet scheduled
        var newActiveJobs = activeJobs.Where(a => !scheduledQuartzJobs.Any(q => q.Name.AsGuid().Equals(a.Guid)));
        foreach (var job in newActiveJobs)
        {
            const string errorSchedulingStatus = "Error scheduling Job";
            try
            {
                IJobDetail jobDetail = service.BuildQuartzJob(job);
                if (jobDetail == null)
                {
                    continue;
                }

                ITrigger jobTrigger = service.BuildQuartzTrigger(job);

                // Schedule the job (unless the cron expression is set to never run for an on-demand job like rebuild streaks)
                if (job.CronExpression != Model.ServiceJob.NeverScheduledCronExpression)
                {
                    scheduler.ScheduleJob(jobDetail, jobTrigger);
                    //job.LastStatusMessage = "Scheduled";
                    //await repository.SaveChangesAsync(ct);
                    jobsScheduleUpdated++;
                }

                // if the last status was an error, but we now loaded successful, clear the error
                if (job.LastStatus == errorSchedulingStatus)
                {
                    job.LastStatusMessage = string.Empty;
                    job.LastStatus = string.Empty;
                    await repository.SaveChangesAsync(ct);
                }
            }
            catch (Exception ex)
            {
                await HandleAndLogError(job, errorSchedulingStatus, ex, ct);
            }
        }

        // reload the jobs in case any where added/removed
        scheduledQuartzJobs = (await scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup(), ct))
            .Where(jobKey => jobKey.Group != "System").ToList();
        foreach (var jobKey in scheduledQuartzJobs)
        {
            var triggersOfJob = await scheduler.GetTriggersOfJob(jobKey, ct);
            var jobCronTrigger = triggersOfJob.OfType<ICronTrigger>().FirstOrDefault();
            var activeJob = activeJobs.FirstOrDefault(a => a.Guid.Equals(jobKey.Name.AsGuid()));
            if (jobCronTrigger == null || activeJob == null)
            {
                continue;
            }

            bool rescheduleJob = false;

            // fix up the schedule if it has changed
            if (activeJob.CronExpression != jobCronTrigger.CronExpressionString)
            {
                rescheduleJob = true;
            }
            else
            {
                // update the job detail if it has changed
                IJobDetail scheduledJobDetail = await scheduler.GetJobDetail(jobKey, ct);
                var activeJobType = activeJob.GetCompiledType();

                if (scheduledJobDetail != null && activeJobType != null)
                {
                    if (scheduledJobDetail.JobType != activeJobType)
                    {
                        rescheduleJob = true;
                    }
                }
            }

            if (rescheduleJob)
            {
                const string errorReschedulingStatus = "Error re-scheduling Job";
                try
                {
                    ITrigger newJobTrigger = service.BuildQuartzTrigger(activeJob);
                    bool deletedSuccessfully = await scheduler.DeleteJob(jobKey, ct);
                    await scheduler.RescheduleJob(jobCronTrigger.Key, newJobTrigger, ct);
                    jobsScheduleUpdated++;

                    if (activeJob.LastStatus == errorReschedulingStatus)
                    {
                        activeJob.LastStatusMessage = string.Empty;
                        activeJob.LastStatus = string.Empty;
                        await repository.SaveChangesAsync(ct);
                    }
                    
                    if (deletedSuccessfully)
                    {
                        Result += $"Successfully unscheduled job:{activeJob.Name} schedule(s)";
                    }
                }
                catch (Exception ex)
                {
                    await HandleAndLogError(activeJob, errorReschedulingStatus, ex, ct);
                }
            }
        }
        
        // update the last run time
        Result = string.Empty;

        if (jobsDeleted > 0)
        {
            Result += $"Deleted {jobsDeleted} job schedule(s)";
        }

        if (jobsScheduleUpdated > 0)
        {
            Result += (Result.IsNullOrEmpty() ? "" : " and ") +
                      $"Updated {jobsScheduleUpdated} schedule(s)";
        }
        
        logger.LogInformation(Result);
    }

    private async Task HandleAndLogError(
        ServiceJob job, string errorStatus, Exception ex, CancellationToken ct)
    {
        logger.LogError(ex.Message);
        // create a friendly error message
        string message = string.Format("Error scheduling the job: {0}.\n\n{2}", job.Name,
            job.Assembly, ex.Message);
        job.LastStatusMessage = message;
        job.LastStatus = errorStatus;
        await repository.SaveChangesAsync(ct);
    }
}