using ReworkGuard.Domain.Calendar;
using ReworkGuard.Domain.Common;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Domain.Entities;

/// <summary>
/// Hours worked on a task. The work type is fixed at creation: corrective hours can never become regular hours.
/// Create through <see cref="WorkTask"/> so the task status rules apply.
/// </summary>
public class TimeLog : IVersioned
{
    public const decimal MaxHoursPerDay = 24m;

    private TimeLog()
    {
    }

    private TimeLog(int taskId, int employeeId, DateOnly workDate, decimal hours, WorkType workType, int? bugId, string? note, DateTime createdAtUtc)
    {
        TaskId = taskId;
        EmployeeId = employeeId;
        WorkDate = workDate;
        Hours = hours;
        WorkType = workType;
        BugId = bugId;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        ApprovalStatus = ApprovalStatus.Pending;
        CreatedAtUtc = createdAtUtc;
    }

    public int Id { get; private set; }

    public int TaskId { get; private set; }

    public WorkTask Task { get; private set; } = null!;

    public int EmployeeId { get; private set; }

    public Employee Employee { get; private set; } = null!;

    public DateOnly WorkDate { get; private set; }

    public decimal Hours { get; private set; }

    public WorkType WorkType { get; private set; }

    public int? BugId { get; private set; }

    public Bug? Bug { get; private set; }

    public ApprovalStatus ApprovalStatus { get; private set; }

    public int? ApprovedByEmployeeId { get; private set; }

    public DateTime? ApprovedAtUtc { get; private set; }

    public string? Note { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public int Version { get; set; }

    internal static TimeLog CreateRegular(int taskId, int employeeId, DateOnly workDate, decimal hours, string? note, decimal hoursAlreadyLoggedThatDay, DateTime utcNow)
    {
        Validate(workDate, hours, hoursAlreadyLoggedThatDay, utcNow);
        return new TimeLog(taskId, employeeId, workDate, hours, WorkType.Regular, bugId: null, note, utcNow);
    }

    internal static TimeLog CreateCorrective(int taskId, int bugId, int employeeId, DateOnly workDate, decimal hours, string? note, decimal hoursAlreadyLoggedThatDay, DateTime utcNow)
    {
        Validate(workDate, hours, hoursAlreadyLoggedThatDay, utcNow);
        return new TimeLog(taskId, employeeId, workDate, hours, WorkType.Corrective, bugId, note, utcNow);
    }

    internal static TimeLog CreateEnhancement(int taskId, int employeeId, DateOnly workDate, decimal hours, string? note, decimal hoursAlreadyLoggedThatDay, DateTime utcNow)
    {
        Validate(workDate, hours, hoursAlreadyLoggedThatDay, utcNow);
        return new TimeLog(taskId, employeeId, workDate, hours, WorkType.Enhancement, bugId: null, note, utcNow);
    }

    public void Approve(int approverId, DateTime utcNow) => Decide(ApprovalStatus.Approved, approverId, utcNow);

    public void Reject(int approverId, DateTime utcNow) => Decide(ApprovalStatus.Rejected, approverId, utcNow);

    private void Decide(ApprovalStatus decision, int approverId, DateTime utcNow)
    {
        if (approverId == EmployeeId)
        {
            throw new SelfApprovalException();
        }

        if (ApprovalStatus != ApprovalStatus.Pending)
        {
            throw new DomainException("timelog.already_decided", $"This time log is already {ApprovalStatus}.");
        }

        ApprovalStatus = decision;
        ApprovedByEmployeeId = approverId;
        ApprovedAtUtc = utcNow;
    }

    private static void Validate(DateOnly workDate, decimal hours, decimal hoursAlreadyLoggedThatDay, DateTime utcNow)
    {
        if (hours <= 0 || hours > MaxHoursPerDay || !Precision.HasAtMostTwoDecimals(hours))
        {
            throw new DomainException("timelog.invalid_hours", "Hours must be greater than 0, at most 24, with at most two decimals.");
        }

        if (workDate > WorkTimeZone.ToWorkDate(utcNow))
        {
            throw new DomainException("timelog.future_date", "Time cannot be logged for a future date.");
        }

        if (hoursAlreadyLoggedThatDay + hours > MaxHoursPerDay)
        {
            throw new DomainException(
                "timelog.daily_limit",
                $"This would bring your total for {workDate:yyyy-MM-dd} to {hoursAlreadyLoggedThatDay + hours} hours; the limit is {MaxHoursPerDay}.");
        }
    }
}
