namespace ReworkGuard.Infrastructure.Persistence;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Apply pending EF Core migrations at startup (development convenience; production runs them in the deploy step).</summary>
    public bool MigrateOnStartup { get; set; }

    /// <summary>Seed demo data into an empty database at startup.</summary>
    public bool SeedOnStartup { get; set; }

    /// <summary>Password given to every seeded user. Required when seeding; supplied via user-secrets or environment.</summary>
    public string? SeedPassword { get; set; }
}
