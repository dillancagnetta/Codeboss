using System;
using CodeBoss.Jobs.Abstractions;
using CodeBoss.Jobs.Jobs;
using CodeBoss.Jobs.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Quartz.Impl.Matchers;

namespace CodeBoss.Jobs;

public static class ConfigureServices
{
    public static IServiceCollection AddCodeBossJobs(
        this IServiceCollection services,
        IConfiguration configuration,
        Type repo,
        bool productionMode = false,
        bool registeredJobListener = false,
        bool isMultiTenantMode = false)
    {
        if (repo == null)
        {
            throw new ArgumentNullException(nameof(repo), 
                "Repository type cannot be null and must implement IServiceJobRepository interface." +
                " Please provide a valid repository type.");
        }
        
        services.Configure<QuartzOptions>(configuration.GetSection(nameof(QuartzOptions)));

        // in test mode, run every minute, otherwise run every 15mins
        var cronExpression = productionMode ? "0 0/15 * * * ?" : "0 * * ? * *" ;
        services.AddQuartz(q =>
        {
            q.UseSimpleTypeLoader();
            q.UseInMemoryStore();
            q.UseDefaultThreadPool(tp => tp.MaxConcurrency = 10);
            
            if (isMultiTenantMode)
            {
                q.ScheduleJob<MultiTenantJobPulse>(trigger => trigger
                    .WithIdentity("MultiTenantJobPulse" , "System")
                    .StartNow()
                    .WithCronSchedule(cronExpression)
                    .WithDescription("Main MultiTenant CodeBoss Jobs Processor"));
            }
            else
            {
                q.ScheduleJob<JobPulse>(trigger => trigger
                    .WithIdentity("JobPulse" , "System")
                    .StartNow()
                    .WithCronSchedule(cronExpression)
                    .WithDescription("Main CodeBoss Jobs Processor"));
            }
           
            if (registeredJobListener)
            {
                q.AddJobListener(q =>
                {
                    var listener = q.GetRequiredService<ICodeBossJobListener>();
                    return listener;
                }, EverythingMatcher<JobKey>.AllJobs()); 
            }
        });

        services.AddQuartzHostedService(options =>
        {
            options.WaitForJobsToComplete = true;
        });
        
        // Allows specifying custom IServiceJobRepository implementation
        services.AddTransient(typeof(IServiceJobRepository), repo);
        services.AddScoped<IServiceJobService, ServiceJobQuartzService>();

        return services;
    }

}