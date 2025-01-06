using System;
using CodeBoss.Jobs.Abstractions;
using CodeBoss.Jobs.Jobs;
using CodeBoss.Jobs.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace CodeBoss.Jobs;

public static class ConfigureServices
{
    public static IServiceCollection AddCodeBossJobs(this IServiceCollection services, IConfiguration configuration,
        Type repo)
    {
        services.Configure<QuartzOptions>(configuration.GetSection(nameof(QuartzOptions)));

        services.AddQuartz(q =>
        {
            q.UseSimpleTypeLoader();
            q.UseInMemoryStore();
            q.UseDefaultThreadPool(tp => tp.MaxConcurrency = 10);
                
            q.ScheduleJob<JobPulse>(trigger => trigger
                .WithIdentity("JobPulse")
                .StartNow()
                 // Run every minute
                .WithCronSchedule("0 * * ? * *")
                .WithDescription("Main CodeBoss Jobs Processor"));
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