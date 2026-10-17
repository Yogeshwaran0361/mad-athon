using MySqlConnector;
using ReworkGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MySql;

namespace ReworkGuard.Tests.Support;

/// <summary>
/// One MySQL 8 container for the whole test run. Each test gets its own freshly migrated database.
/// </summary>
public sealed class MySqlContainerFixture : IAsyncLifetime
{
    private readonly MySqlContainer _container = new MySqlBuilder("mysql:8.0")
        .WithUsername("root")
        .WithPassword("test-only-root")
        .WithTmpfsMount("/var/lib/mysql")
        // Long statement samples in performance_schema let the opt-in performance test EXPLAIN the real EF queries.
        .WithCommand(
            "--skip-log-bin",
            "--innodb-flush-log-at-trx-commit=0",
            "--performance-schema-max-sql-text-length=16384",
            "--performance-schema-max-digest-length=16384",
            "--max-digest-length=16384")
        .Build();

    private int _databaseCounter;

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public async Task<string> CreateMigratedDatabaseAsync()
    {
        var name = $"rg_test_{Interlocked.Increment(ref _databaseCounter)}_{Guid.NewGuid():N}"[..40];
        var builder = new MySqlConnectionStringBuilder(_container.GetConnectionString()) { Database = name };
        var connectionString = builder.ConnectionString;

        await using var db = CreateContext(connectionString);
        await db.Database.MigrateAsync();
        return connectionString;
    }

    public static async Task DropDatabaseAsync(ApplicationDbContext db)
    {
        await db.Database.EnsureDeletedAsync();
        await db.DisposeAsync();
    }

    public static ApplicationDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseReworkGuardMySql(connectionString).Options);
}

[CollectionDefinition(Name)]
public sealed class MySqlTestGroup : ICollectionFixture<MySqlContainerFixture>
{
    public const string Name = "MySQL";
}
