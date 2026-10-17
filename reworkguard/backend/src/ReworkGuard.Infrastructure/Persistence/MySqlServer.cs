using Microsoft.EntityFrameworkCore;

namespace ReworkGuard.Infrastructure.Persistence;

public static class MySqlServer
{
    /// <summary>The one place the MySQL provider is configured, shared by the app, design-time tooling and tests.</summary>
    public static DbContextOptionsBuilder<TContext> UseReworkGuardMySql<TContext>(this DbContextOptionsBuilder<TContext> options, string connectionString)
        where TContext : DbContext =>
        options.UseMySql(connectionString, Version, mysql => mysql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));

    /// <summary>Pinned so the model and migrations do not depend on reaching a server at startup.</summary>
    public static readonly ServerVersion Version = new MySqlServerVersion(new Version(8, 0, 36));
}
