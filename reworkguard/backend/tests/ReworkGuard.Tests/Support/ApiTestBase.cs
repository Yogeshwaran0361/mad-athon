using Microsoft.EntityFrameworkCore;
using ReworkGuard.Infrastructure.Persistence;

namespace ReworkGuard.Tests.Support;

/// <summary>Per-test fresh database, a hosted API on top of it, and a data builder.</summary>
[Collection(MySqlTestGroup.Name)]
public abstract class ApiTestBase : IAsyncLifetime
{
    private readonly MySqlContainerFixture _mysql;

    protected ApiTestBase(MySqlContainerFixture mysql)
    {
        _mysql = mysql;
    }

    protected ApplicationDbContext Db { get; private set; } = null!;

    protected ApiFactory Api { get; private set; } = null!;

    protected TestData Data { get; private set; } = null!;

    protected TestClock Clock { get; } = new() { UtcNow = new DateTime(2026, 3, 10, 9, 0, 0, DateTimeKind.Utc) };

    public async Task InitializeAsync()
    {
        var connectionString = await _mysql.CreateMigratedDatabaseAsync();
        Db = MySqlContainerFixture.CreateContext(connectionString);
        Data = new TestData(Db);
        Api = new ApiFactory(connectionString, Clock);
    }

    public async Task DisposeAsync()
    {
        await Api.DisposeAsync();
        await MySqlContainerFixture.DropDatabaseAsync(Db);
    }

    /// <summary>A context with no tracked state, for reading what the API wrote.</summary>
    protected ApplicationDbContext FreshDb() => MySqlContainerFixture.CreateContext(Db.Database.GetConnectionString()!);
}
