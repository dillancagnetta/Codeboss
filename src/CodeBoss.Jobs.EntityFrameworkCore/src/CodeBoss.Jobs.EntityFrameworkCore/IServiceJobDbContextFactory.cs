using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace CodeBoss.Jobs.EntityFrameworkCore;

/// <summary>
/// Supplies the <see cref="DbContext"/> holding a tenant's <c>ServiceJob</c> rows.
///
/// <para>This is the only thing a consumer has to write to use
/// <see cref="EfServiceJobRepository{TContext}"/>. In a single-database deployment it returns the
/// same context regardless of tenant; in a database-per-tenant deployment it routes on
/// <paramref name="tenantId"/>.</para>
/// </summary>
/// <typeparam name="TContext">The context type. Must expose a <c>DbSet</c> of the job entities.</typeparam>
public interface IServiceJobDbContextFactory<TContext> where TContext : DbContext
{
    /// <summary>
    /// Creates a context for the given tenant, or for the single database when
    /// <paramref name="tenantId"/> is null.
    /// </summary>
    /// <remarks>
    /// <b>The caller owns and disposes the returned context</b>, so return a NEW instance — typically
    /// from <c>IDbContextFactory&lt;T&gt;.CreateDbContextAsync()</c>. Do not return a scoped context
    /// that something else also owns: the repository will dispose it out from under that owner.
    /// </remarks>
    ValueTask<TContext> CreateAsync(int? tenantId, CancellationToken ct = default);
}
