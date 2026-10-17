namespace ReworkGuard.Domain.Lifecycle;

public static class ModuleHealth
{
    /// <summary>A module whose tasks have been reopened this many times in total is flagged as chronic.</summary>
    public const int ChronicReopenThreshold = 3;

    public static bool IsChronic(int totalReopens) => totalReopens >= ChronicReopenThreshold;
}
