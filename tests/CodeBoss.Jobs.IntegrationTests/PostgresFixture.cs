using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace CodeBoss.Jobs.IntegrationTests;

/// <summary>
/// One PostgreSQL container shared by every test in the collection, holding both the Quartz
/// <c>qrtz_*</c> tables and the library's own job tables.
///
/// <para>These tests exist because the riskiest parts of the library are precisely the parts a unit
/// test cannot reach: the clustered ADO job store, <c>useProperties</c> serialisation of the job data
/// map, and whether two scheduler nodes sharing a store each fire a trigger once or twice.</para>
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("codeboss_jobs")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // EF FIRST. EnsureCreated is a no-op when the database already contains ANY table, so
        // running Quartz's DDL before it would silently skip creating the job tables.
        await using (var db = CreateDbContext())
        {
            await db.Database.EnsureCreatedAsync();
        }

        await ExecuteAsync(ReadEmbedded("tables_postgres.sql"));
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    public JobsDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<JobsDbContext>().UseNpgsql(ConnectionString).Options);

    /// <summary>Wipes scheduler and job state so each test starts from a known point.</summary>
    public async Task ResetAsync()
    {
        await ExecuteAsync("""
            TRUNCATE qrtz_fired_triggers, qrtz_simple_triggers, qrtz_simprop_triggers,
                     qrtz_cron_triggers, qrtz_blob_triggers, qrtz_triggers, qrtz_job_details,
                     qrtz_paused_trigger_grps, qrtz_scheduler_state, qrtz_locks CASCADE;
            """);

        await using var db = CreateDbContext();
        await db.Database.ExecuteSqlRawAsync("""TRUNCATE "ServiceJobHistory", "ServiceJobs" RESTART IDENTITY CASCADE;""");

        // Quartz's cluster lock rows are seeded by the schema script; TRUNCATE removed them.
        await ExecuteAsync("""
            INSERT INTO qrtz_locks (sched_name, lock_name) VALUES
                ('TestScheduler', 'TRIGGER_ACCESS'), ('TestScheduler', 'STATE_ACCESS')
            ON CONFLICT DO NOTHING;
            """);
    }

    public async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<long> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static string ReadEmbedded(string name)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resource = assembly.GetManifestResourceNames().Single(n => n.EndsWith(name, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
