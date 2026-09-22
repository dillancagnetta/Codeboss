using System;
using System.Threading;
using CodeBoss.Jobs.Abstractions;
using CodeBoss.Jobs.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;
using Quartz.Simpl;
using Quartz.Spi;

namespace CodeBoss.Jobs.Jobs;

/// <summary>
/// Quartz job factory that establishes the ambient tenant context on the job's DI scope before the
/// job instance is resolved, by delegating to the registered <see cref="IJobTenantScopeInitializer"/>.
///
/// <para>Quartz's <see cref="MicrosoftDependencyInjectionJobFactory"/> calls
/// <see cref="ConfigureScope"/> with the freshly created <see cref="IServiceScope"/> and only then
/// resolves the job type from that scope. Anything set here is therefore visible to every service
/// the job takes in its constructor — which is what a tenant-aware <c>DbContext</c> needs, since it
/// chooses its connection string at construction.</para>
///
/// <para>Registered automatically by <c>AddCodeBossJobs</c> when
/// <c>CodeBossJobsOptions.IsMultiTenantMode</c> is set, via <c>TryAddSingleton</c> — a consumer that
/// registers its own <see cref="IJobFactory"/> beforehand keeps it. Derive from this class rather
/// than replacing it, or tenant jobs will run with no tenant context.</para>
/// </summary>
public class TenantScopedJobFactory(
    IServiceProvider serviceProvider,
    IOptions<QuartzOptions> options,
    ILogger<TenantScopedJobFactory> logger)
    : MicrosoftDependencyInjectionJobFactory(serviceProvider, options)
{
    private int _missingInitializerWarned;

    protected override void ConfigureScope(IServiceScope scope, TriggerFiredBundle bundle, IScheduler scheduler)
    {
        base.ConfigureScope(scope, bundle, scheduler);

        var jobKey = bundle.JobDetail.Key;

        // System jobs (the pulse) carry no tenant and must not be given one.
        if (jobKey.Group == JobGroups.System) return;

        var map = bundle.JobDetail.JobDataMap;
        if (!map.ContainsKey(ServiceJobQuartzService.TenantIdKey)) return;

        var tenantId = map.GetIntValue(ServiceJobQuartzService.TenantIdKey);
        if (tenantId <= 0)
        {
            logger.LogWarning(
                "Job {JobKey} carries a non-positive tenant id {TenantId}; running with no tenant context.",
                jobKey, tenantId);
            return;
        }

        var initializer = scope.ServiceProvider.GetService<IJobTenantScopeInitializer>();
        if (initializer is null)
        {
            // Warn once. This fires on every tenant job, so an unconditional warning would bury the log.
            if (Interlocked.Exchange(ref _missingInitializerWarned, 1) == 0)
            {
                logger.LogWarning(
                    "Tenant job {JobKey} fired for tenant {TenantId} but no {Interface} is registered. " +
                    "Tenant jobs will run WITHOUT tenant context. Register one as scoped.",
                    jobKey, tenantId, nameof(IJobTenantScopeInitializer));
            }

            return;
        }

        try
        {
            initializer.Initialize(scope.ServiceProvider, tenantId, jobKey);
            logger.LogDebug("Tenant context set to {TenantId} for job {JobKey}.", tenantId, jobKey);
        }
        catch (Exception ex)
        {
            // Deliberately fatal to the fire. Running against the wrong tenant's database writes bad
            // data silently; a failed fire is recorded on the job row and is visible.
            logger.LogError(ex, "Failed to establish tenant context {TenantId} for job {JobKey}; aborting the fire.",
                tenantId, jobKey);
            throw;
        }
    }
}
