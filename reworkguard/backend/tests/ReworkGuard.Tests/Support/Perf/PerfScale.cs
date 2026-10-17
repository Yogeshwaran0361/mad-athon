using System.Globalization;

namespace ReworkGuard.Tests.Support.Perf;

/// <summary>How much data the volume seeder writes. <see cref="Full"/> is the brief's target volume.</summary>
public sealed record PerfScale(int Employees, int Tasks, int RegularLogs, int CorrectiveLogs, int Bugs, int NotificationsPerEmployee)
{
    /// <summary>200 employees, 5,000 tasks, 20,000 time logs (16,000 regular + 4,000 corrective), 1,500 bugs.</summary>
    public static PerfScale Full { get; } = new(200, 5000, 16000, 4000, 1500, 30);

    public static PerfScale Of(double factor) => factor >= 1
        ? Full
        : new(
            Math.Max(40, (int)(Full.Employees * factor)),
            (int)(Full.Tasks * factor),
            (int)(Full.RegularLogs * factor),
            (int)(Full.CorrectiveLogs * factor),
            (int)(Full.Bugs * factor),
            Math.Max(5, (int)(Full.NotificationsPerEmployee * factor)));

    /// <summary>RG_PERF_SCALE: a factor of the full volume (default 0.25 for the CI guard; 1 = full).</summary>
    public static PerfScale FromEnvironment()
    {
        var text = Environment.GetEnvironmentVariable("RG_PERF_SCALE");
        return Of(double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var factor) && factor > 0 ? factor : 0.25);
    }
}
