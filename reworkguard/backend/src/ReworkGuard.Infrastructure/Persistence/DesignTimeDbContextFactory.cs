using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ReworkGuard.Infrastructure.Persistence;

/// <summary>
/// Used by `dotnet ef`. Reads the connection string from REWORKGUARD_MIGRATIONS_CONNECTION when commands need a
/// live database (database update); adding migrations needs no connection.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("REWORKGUARD_MIGRATIONS_CONNECTION") ?? "Server=localhost;Database=reworkguard";
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseReworkGuardMySql(connectionString)
            .Options;
        return new ApplicationDbContext(options);
    }
}
