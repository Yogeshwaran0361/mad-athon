using Hangfire;
using ReworkGuard.Domain.Calendar;

namespace ReworkGuard.Infrastructure.Jobs;

/// <summary>The schedule. Times are Asia/Kolkata so "09:00" means 09:00 for the people receiving reminders.</summary>
public static class RecurringJobs
{
    public const string OutboxDelivery = "outbox-delivery";
    public const string DeductionWindows = "deduction-windows";
    public const string WarrantySweep = "warranty-sweep";
    public const string Reminders = "daily-reminders";

    public static void Register(IRecurringJobManager manager, JobsOptions options)
    {
        var ist = new RecurringJobOptions { TimeZone = WorkTimeZone.Zone };
        manager.AddOrUpdate<JobRunner>(OutboxDelivery, r => r.DeliverOutboxAsync(CancellationToken.None), options.OutboxCron, ist);
        manager.AddOrUpdate<JobRunner>(DeductionWindows, r => r.DeductionWindowsAsync(CancellationToken.None), options.DeductionWindowsCron, ist);
        manager.AddOrUpdate<JobRunner>(WarrantySweep, r => r.WarrantySweepAsync(CancellationToken.None), options.WarrantySweepCron, ist);
        manager.AddOrUpdate<JobRunner>(Reminders, r => r.RemindersAsync(CancellationToken.None), options.RemindersCron, ist);
    }
}
