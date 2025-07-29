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
        Action<CodeBossJobsOptions> configure)
    {
        var options = new CodeBossJobsOptions();
        configure?.Invoke(options);
        
        ArgumentNullException.ThrowIfNull(options.Repo, "Repository type cannot be null and must implement IServiceJobRepository interface." +
                                                        " Please provide a valid repository type.");
        
        services.Configure<QuartzOptions>(configuration.GetSection(nameof(QuartzOptions)));

        // in test mode, run every minute, otherwise run every 15mins
        var cronExpression = options.ProductionMode ? "0 0/15 * * * ?" : "0 * * ? * *" ;
        services.AddQuartz(q =>
        {
            q.UseSimpleTypeLoader();
            q.UseInMemoryStore();
            q.UseDefaultThreadPool(tp => tp.MaxConcurrency = 10);
            
            if (options.IsMultiTenantMode)
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
           
            if (options.RegisteredJobListener)
            {
                q.AddJobListener(q =>
                {
                    var listener = q.GetRequiredService<ICodeBossJobListener>();
                    return listener;
                }, EverythingMatcher<JobKey>.AllJobs()); 
            }
        });

        services.AddQuartzHostedService(opt =>
        {
            opt.WaitForJobsToComplete = true;
        });
        
        // Allows specifying custom IServiceJobRepository implementation
        services.AddTransient(typeof(IServiceJobRepository), options.Repo);
        services.AddScoped<IServiceJobService, ServiceJobQuartzService>();

        return services;
    }

}