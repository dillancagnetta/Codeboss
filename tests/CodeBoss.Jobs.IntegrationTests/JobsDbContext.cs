using CodeBoss.Jobs.EntityFrameworkCore;
using CodeBoss.Jobs.Model;
using Microsoft.EntityFrameworkCore;

namespace CodeBoss.Jobs.IntegrationTests;

public sealed class JobsDbContext(DbContextOptions<JobsDbContext> options) : DbContext(options)
{
    public DbSet<ServiceJob> ServiceJobs => Set<ServiceJob>();
    public DbSet<ServiceJobHistory> ServiceJobHistory => Set<ServiceJobHistory>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<ServiceJob>(job =>
        {
            job.ToTable("ServiceJobs");
            job.HasKey(x => x.Id);
            job.Ignore(x => x.CronDescription);
            job.Ignore(x => x.LastRunStatus);
            job.Property(x => x.JobParameters)
                .HasConversion(
                    v => v == null ? null : string.Join(';', v.Select(kv => $"{kv.Key}={kv.Value}")),
                    v => string.IsNullOrEmpty(v)
                        ? new Dictionary<string, string>()
                        : v.Split(';', StringSplitOptions.RemoveEmptyEntries)
                           .Select(pair => pair.Split('=', 2))
                           .ToDictionary(p => p[0], p => p.Length > 1 ? p[1] : string.Empty));
            job.HasMany(x => x.ServiceJobHistory).WithOne(x => x.ServiceJob).HasForeignKey(x => x.ServiceJobId);
        });

        builder.Entity<ServiceJobHistory>(history =>
        {
            history.ToTable("ServiceJobHistory");
            history.HasKey(x => x.Id);
        });
    }
}

/// <summary>
/// Single-database routing: the tenant id is carried through but every tenant lives in this one
/// database, which is the shape most consumers start with.
/// </summary>
public sealed class FixtureDbContextFactory(PostgresFixture fixture) : IServiceJobDbContextFactory<JobsDbContext>
{
    public ValueTask<JobsDbContext> CreateAsync(int? tenantId, CancellationToken ct = default)
        => ValueTask.FromResult(fixture.CreateDbContext());
}
