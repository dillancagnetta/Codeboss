using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CodeBoss.Jobs.Abstractions;
using CodeBoss.Jobs.Jobs;
using CodeBoss.Jobs.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Quartz;
using Quartz.Impl.Matchers;
using Quartz.Spi;

namespace CodeBoss.Jobs;

public static class ConfigureServices
{
    /// <summary>
    /// Registers Quartz, the reconciliation pulse, and the job repository/service.
    /// </summary>
    /// <param name="configuration">
    /// Binds the <c>QuartzOptions</c> configuration section when present. May be null when Quartz is
    /// configured entirely in code through <see cref="CodeBossJobsOptions.ConfigureQuartz"/>.
    /// </param>
    public static IServiceCollection AddCodeBossJobs(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<CodeBossJobsOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new CodeBossJobsOptions();
        configure.Invoke(options);

        ArgumentNullException.ThrowIfNull(options.Repo, "Repository type cannot be null and must implement IServiceJobRepository interface." +
                                                        " Please provide a valid repository type.");

        if (configuration is not null)
        {
            services.Configure<QuartzOptions>(configuration.GetSection(nameof(QuartzOptions)));
        }

        services.Configure(configure);

        if (options.IsMultiTenantMode)
        {
            // TryAdd, and before AddQuartz: Quartz registers MicrosoftDependencyInjectionJobFactory
            // with its own TryAddSingleton, so whichever IJobFactory is registered first wins. A
            // consumer that registered a factory ahead of this call keeps it.
            //
            // The scheduler resolves IJobFactory from DI (ServiceCollectionSchedulerFactory
            // .InstantiateType<T> tries the container before the configured
            // quartz.scheduler.jobFactory.type), so this registration is what actually takes effect.
            services.TryAddSingleton<IJobFactory, TenantScopedJobFactory>();
        }

        services.AddQuartz(q =>
        {
            q.UseSimpleTypeLoader();
            q.UseDefaultThreadPool(tp => tp.MaxConcurrency = options.ConcurrentSchedulerOperations);

            // Default store. ConfigureQuartz runs last and may replace it: both this and
            // UsePersistentStore write quartz.jobStore.type, so the last writer wins.
            q.UseInMemoryStore();

            AddPulse(q, options);

            if (options.ShouldUseDefaultStatusListener)
            {
                q.AddJobListener(sp => new CodeBossJobStatusListener(
                        sp.GetRequiredService<IServiceScopeFactory>(),
                        sp.GetRequiredService<ILogger<CodeBossJobStatusListener>>()),
                    EverythingMatcher<JobKey>.AllJobs());
            }

            if (options.RegisteredJobListener)
            {
                q.AddJobListener(sp => sp.GetRequiredService<ICodeBossJobListener>(), EverythingMatcher<JobKey>.AllJobs());
            }

            options.ConfigureQuartz?.Invoke(q);
        });

        services.AddQuartzHostedService(opt =>
        {
            opt.WaitForJobsToComplete = true;
        });

        // Allows specifying custom IServiceJobRepository implementation
        services.AddTransient(typeof(IServiceJobRepository), options.Repo);
        services.AddScoped<IServiceJobService, ServiceJobQuartzService>();

        // Out-of-band control (run now, sync now, interrupt). TryAdd so a consumer can substitute
        // its own. Resolvable on a host that does not run the scheduler, as long as it shares a
        // clustered persistent store with the one that does.
        services.TryAddScoped<IJobCommands, QuartzJobCommands>();

        // Empty unless the consumer calls AddCodeBossJob/AddCodeBossJobsFromAssembly. An empty
        // registry still resolves legacy rows via its Type.GetType fallback.
        services.TryAddSingleton<IJobTypeRegistry, JobTypeRegistry>();

        return services;
    }

    /// <summary>
    /// Overload for hosts that configure Quartz entirely in code via
    /// <see cref="CodeBossJobsOptions.ConfigureQuartz"/> and have no <c>QuartzOptions</c>
    /// configuration section to bind.
    /// </summary>
    public static IServiceCollection AddCodeBossJobs(
        this IServiceCollection services,
        Action<CodeBossJobsOptions> configure)
        => services.AddCodeBossJobs(configuration: null, configure);

    /// <summary>
    /// Registers a job type under its stable id, and in DI so its dependencies are verified at
    /// startup rather than at first fire.
    /// </summary>
    /// <remarks>
    /// Store the id — <see cref="JobDefinitionIdAttribute"/> if present, otherwise the full type name
    /// — in <c>ServiceJob.Class</c>. Registered types resolve without reflection, so renaming or
    /// moving the class no longer breaks its rows.
    /// </remarks>
    public static IServiceCollection AddCodeBossJob<TJob>(this IServiceCollection services)
        where TJob : class, ICodeBossJob
        => services.AddCodeBossJob(typeof(TJob));

    /// <inheritdoc cref="AddCodeBossJob{TJob}"/>
    public static IServiceCollection AddCodeBossJob(this IServiceCollection services, Type jobType)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(jobType);

        if (!typeof(ICodeBossJob).IsAssignableFrom(jobType))
        {
            throw new ArgumentException($"'{jobType}' does not implement {nameof(ICodeBossJob)}.", nameof(jobType));
        }

        var id = jobType.GetCustomAttribute<JobDefinitionIdAttribute>()?.Id ?? jobType.FullName;

        services.AddSingleton(new JobTypeRegistration(id, jobType));
        services.TryAddScoped(jobType);

        return services;
    }

    /// <summary>
    /// Registers every concrete <c>ICodeBossJob</c> in an assembly.
    /// </summary>
    public static IServiceCollection AddCodeBossJobsFromAssembly(this IServiceCollection services, Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assembly);

        foreach (var jobType in GetLoadableTypes(assembly))
        {
            if (jobType.IsAbstract || jobType.IsInterface) continue;
            if (!typeof(ICodeBossJob).IsAssignableFrom(jobType)) continue;

            services.AddCodeBossJob(jobType);
        }

        return services;
    }

    /// <summary>
    /// Types from an assembly, skipping any that cannot be loaded.
    /// </summary>
    /// <remarks>
    /// A plain <c>Assembly.GetTypes()</c> throws <see cref="ReflectionTypeLoadException"/> outright if
    /// ANY single type fails to load — one unresolvable reference in an unrelated class would take
    /// down registration for every job in the assembly. The partial results on the exception are the
    /// types that did load.
    /// </remarks>
    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null);
        }
    }

    /// <summary>
    /// Registers the pulse as a DURABLE job with a separate repeating trigger.
    ///
    /// <para>Durable matters: a job with no trigger is removed from the store unless it is durable,
    /// and an on-demand <c>TriggerJob</c> against the pulse (a "sync now" request) needs the job to
    /// exist independently of its schedule.</para>
    ///
    /// <para>The trigger repeats on a fixed interval rather than a cron expression. A missed pulse
    /// is not worth catching up on — the next run reconciles the full current state anyway — so the
    /// misfire instruction drops missed fires and resumes on the next scheduled one.</para>
    /// </summary>
    private static void AddPulse(IServiceCollectionQuartzConfigurator q, CodeBossJobsOptions options)
    {
        var pulseKey = options.IsMultiTenantMode
            ? new JobKey(nameof(MultiTenantJobPulse), JobGroups.System)
            : new JobKey(nameof(JobPulse), JobGroups.System);

        var description = options.IsMultiTenantMode
            ? "Main MultiTenant CodeBoss Jobs Processor"
            : "Main CodeBoss Jobs Processor";

        if (options.IsMultiTenantMode)
        {
            q.AddJob<MultiTenantJobPulse>(pulseKey, j => j.StoreDurably().WithDescription(description));
        }
        else
        {
            q.AddJob<JobPulse>(pulseKey, j => j.StoreDurably().WithDescription(description));
        }

        q.AddTrigger(t => t
            .ForJob(pulseKey)
            .WithIdentity($"{pulseKey.Name}_trigger", JobGroups.System)
            .StartAt(options.PulseOnStartup
                ? DateTimeOffset.UtcNow
                : DateTimeOffset.UtcNow.Add(options.PulseInterval))
            .WithSimpleSchedule(s => s
                .WithInterval(options.PulseInterval)
                .RepeatForever()
                .WithMisfireHandlingInstructionNextWithRemainingCount()));
    }
}
