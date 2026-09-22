using System;
using Quartz;

namespace CodeBoss.Jobs.Abstractions;

/// <summary>
/// Establishes the ambient tenant context on a job's dependency-injection scope.
///
/// <para>Implementations are called by <c>TenantScopedJobFactory</c> immediately after the scope is
/// created and BEFORE the job instance and its constructor dependencies are resolved. That ordering
/// is the whole point: a tenant-aware <c>DbContext</c> or repository resolved into the job's scope
/// picks its connection string at construction time, so the tenant has to be in place first.
/// Setting it inside the job body is too late.</para>
///
/// <para>Register as <b>scoped</b>. Implementations must be synchronous and must not perform I/O —
/// they run on a Quartz worker thread while the fire is in progress. An in-memory tenant lookup is
/// the expected shape.</para>
///
/// <para>Throwing aborts the fire. Prefer that to letting a job run against the wrong database: a
/// job that did not run is visible, a job that quietly wrote to the wrong tenant is not.</para>
/// </summary>
/// <example>
/// <code>
/// public sealed class AppJobTenantScopeInitializer(ITenantLookup lookup) : IJobTenantScopeInitializer
/// {
///     public void Initialize(IServiceProvider scopedProvider, int tenantId, JobKey jobKey)
///     {
///         var accessor = scopedProvider.GetRequiredService&lt;IAppContextAccessor&gt;();
///         accessor.AppContext = lookup.ContextFor(tenantId)
///             ?? throw new InvalidOperationException($"Unknown tenant {tenantId} for job {jobKey}.");
///     }
/// }
/// </code>
/// </example>
public interface IJobTenantScopeInitializer
{
    /// <param name="scopedProvider">The job's scope. Resolve the context holder from here, not from the root provider.</param>
    /// <param name="tenantId">Tenant id read from the job's data map. Always greater than zero.</param>
    /// <param name="jobKey">The job about to run, for diagnostics.</param>
    void Initialize(IServiceProvider scopedProvider, int tenantId, JobKey jobKey);
}
