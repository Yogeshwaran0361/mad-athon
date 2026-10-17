using System.ComponentModel.DataAnnotations;

namespace ReworkGuard.Infrastructure.Jobs;

public sealed class JobsOptions
{
    public const string SectionName = "Jobs";

    /// <summary>Off in integration tests, which call the job services directly.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Whether this instance processes jobs. Turn off on instances that should only serve HTTP.</summary>
    public bool RunServer { get; set; } = true;

    [Range(1, 50)]
    public int WorkerCount { get; set; } = 4;

    /// <summary>Six-field cron (with seconds): in-app pushes and emails go out within this interval.</summary>
    public string OutboxCron { get; set; } = "*/15 * * * * *";

    public string DeductionWindowsCron { get; set; } = "*/5 * * * *";

    /// <summary>Asia/Kolkata time.</summary>
    public string WarrantySweepCron { get; set; } = "30 1 * * *";

    /// <summary>Asia/Kolkata time, weekdays at 09:00.</summary>
    public string RemindersCron { get; set; } = "0 9 * * 1-5";
}
