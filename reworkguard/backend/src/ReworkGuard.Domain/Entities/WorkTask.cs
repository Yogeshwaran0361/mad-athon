using ReworkGuard.Domain.Calendar;
using ReworkGuard.Domain.Common;
using ReworkGuard.Domain.Enums;
using ReworkGuard.Domain.Lifecycle;

namespace ReworkGuard.Domain.Entities;

/// <summary>
/// A deliverable. It is the aggregate root for its assignments, status history and bugs,
/// and the only place that changes task status.
/// </summary>
public class WorkTask : IVersioned
{
    private readonly List<TaskAssignment> _assignments = [];
    private readonly List<TaskStateHistory> _history = [];
    private readonly List<Bug> _bugs = [];

    private WorkTask()
    {
    }

    public WorkTask(Module module, string title, DateOnly committedDate, IEnumerable<int> originalDelivererIds)
    {
        var deliverers = originalDelivererIds.Distinct().ToList();
        if (deliverers.Count == 0)
        {
            throw new DomainException("task.no_assignee", "A task needs at least one assignee.");
        }

        Module = module;
        Title = title;
        CommittedDate = committedDate;
        Status = WorkTaskStatus.Assigned;
        _assignments.AddRange(deliverers.Select(id => new TaskAssignment(id, isOriginalDeliverer: true)));
    }

    public int Id { get; private set; }

    public int ModuleId { get; private set; }

    public Module Module { get; private set; } = null!;

    public string Title { get; private set; } = string.Empty;

    public WorkTaskStatus Status { get; private set; }

    public DateOnly CommittedDate { get; private set; }

    public DateOnly? ActualCompletionDate { get; private set; }

    public int ReopenCount { get; private set; }

    /// <summary>Set when the task is the work for an approved change request. Such tasks take Enhancement hours only.</summary>
    public int? ChangeRequestId { get; private set; }

    public int Version { get; set; }

    public IReadOnlyCollection<TaskAssignment> Assignments => _assignments;

    public IReadOnlyCollection<TaskStateHistory> History => _history;

    public IReadOnlyCollection<Bug> Bugs => _bugs;

    /// <summary>Creates the task that carries an approved change request's work.</summary>
    public static WorkTask ForChangeRequest(Module module, int changeRequestId, string title, DateOnly committedDate, IEnumerable<int> assigneeIds) =>
        new(module, title, committedDate, assigneeIds) { ChangeRequestId = changeRequestId };

    public IEnumerable<int> OriginalDelivererIds =>
        _assignments.Where(a => a.IsOriginalDeliverer).Select(a => a.EmployeeId);

    public bool IsAssignedTo(int employeeId) => _assignments.Any(a => a.EmployeeId == employeeId);

    public bool HasUnverifiedBugs => _bugs.Any(b => b.Status is BugStatus.Open or BugStatus.Fixed);

    /// <summary>A manual status change requested by a user. Closing and reopening have dedicated methods.</summary>
    public TaskStateHistory MoveTo(WorkTaskStatus target, int actorId, string? reason, DateTime utcNow)
    {
        if (target == WorkTaskStatus.Closed)
        {
            throw new InvalidStateTransitionException(Status, target, "use the close action, which checks warranty and bug verification.");
        }

        var history = Transition(target, TransitionTrigger.Manual, actorId, reason, utcNow);
        if (target == WorkTaskStatus.Completed)
        {
            foreach (var bug in _bugs.Where(b => b.Status == BugStatus.Open))
            {
                bug.MarkFixed();
            }
        }

        return history;
    }

    /// <summary>
    /// Closes a completed task. Allowed once every bug is verified, and either the warranty window
    /// has passed or a manager closes it explicitly.
    /// </summary>
    public TaskStateHistory Close(int actorId, bool actorIsManager, int warrantyDays, string? reason, DateTime utcNow)
    {
        TaskStateMachine.EnsureAllowed(Status, WorkTaskStatus.Closed, TransitionTrigger.Manual);

        if (HasUnverifiedBugs)
        {
            throw new InvalidStateTransitionException(Status, WorkTaskStatus.Closed, "every bug on the task must be verified first.");
        }

        var today = WorkTimeZone.ToWorkDate(utcNow);
        var warrantyExpired = WarrantyPolicy.HasExpired(ActualCompletionDate!.Value, warrantyDays, today);
        if (!warrantyExpired && !actorIsManager)
        {
            throw new InvalidStateTransitionException(Status, WorkTaskStatus.Closed, "the warranty window is still open; only a manager can close it early.");
        }

        return Transition(WorkTaskStatus.Closed, TransitionTrigger.Manual, actorId, reason, utcNow);
    }

    /// <summary>
    /// The nightly warranty sweep closes a completed task once its warranty window has passed and every bug is
    /// verified. Returns null (and changes nothing) when the task does not qualify yet.
    /// </summary>
    public TaskStateHistory? CloseAfterWarranty(int warrantyDays, DateTime utcNow)
    {
        if (Status != WorkTaskStatus.Completed || HasUnverifiedBugs || ActualCompletionDate is null
            || !WarrantyPolicy.HasExpired(ActualCompletionDate.Value, warrantyDays, WorkTimeZone.ToWorkDate(utcNow)))
        {
            return null;
        }

        return Transition(WorkTaskStatus.Closed, TransitionTrigger.WarrantyExpired, actorId: null, $"Warranty window of {warrantyDays} days ended", utcNow);
    }

    /// <summary>
    /// Records a defect against this delivered task and reopens it. The only way a task is reopened.
    /// </summary>
    public Bug ReportBug(
        int warrantyDays,
        BugSeverity severity,
        string description,
        DateOnly foundDate,
        int reporterId,
        DateTime utcNow)
    {
        if (ChangeRequestId is not null)
        {
            throw new DomainException("bug.change_request_task", "This task is a change request (paid enhancement work); it cannot take a bug report or be reopened.");
        }

        if (Status is not (WorkTaskStatus.Completed or WorkTaskStatus.Closed))
        {
            throw new DomainException("bug.task_not_delivered", $"Bugs can only be reported on Completed or Closed tasks; this task is {Status}.");
        }

        var completion = ActualCompletionDate!.Value;
        // The only date rule: a defect cannot be found before the work was delivered. Warranty is judged from this date.
        if (foundDate < completion)
        {
            throw new DomainException("bug.found_before_delivery", $"The found date cannot be before the task was completed ({completion:yyyy-MM-dd}).");
        }

        var withinWarranty = WarrantyPolicy.IsWithinWarranty(completion, warrantyDays, foundDate);
        if (Status == WorkTaskStatus.Closed && !withinWarranty)
        {
            throw new DomainException(
                "bug.closed_outside_warranty",
                "This task is closed and its warranty window has passed. Raise it as a change request instead.");
        }

        var bug = new Bug(severity, description, foundDate, reporterId, withinWarranty, utcNow);
        _bugs.Add(bug);
        Transition(WorkTaskStatus.Reopened, TransitionTrigger.BugReported, reporterId, $"Bug reported: {Truncate(description, 200)}", utcNow);
        return bug;
    }

    public TimeLog LogRegularHours(int employeeId, DateOnly workDate, decimal hours, string? note, decimal hoursAlreadyLoggedThatDay, DateTime utcNow)
    {
        EnsureAssigned(employeeId);
        EnsureNotChangeRequest(WorkType.Regular);
        if (Status is not (WorkTaskStatus.Assigned or WorkTaskStatus.InProgress))
        {
            throw new DomainException(
                "timelog.regular_not_allowed",
                $"Regular hours cannot be logged on a task that is {Status}. Work on a delivered or reopened task must be logged as corrective time against its bug.");
        }

        return TimeLog.CreateRegular(Id, employeeId, workDate, hours, note, hoursAlreadyLoggedThatDay, utcNow);
    }

    /// <summary>
    /// Logs corrective hours against an open bug. The first corrective log on a reopened task moves it to UnderFix.
    /// </summary>
    public TimeLog LogCorrectiveHours(int bugId, int employeeId, DateOnly workDate, decimal hours, string? note, decimal hoursAlreadyLoggedThatDay, DateTime utcNow)
    {
        EnsureAssigned(employeeId);
        EnsureNotChangeRequest(WorkType.Corrective);
        var bug = _bugs.SingleOrDefault(b => b.Id == bugId)
            ?? throw new DomainException("timelog.bug_not_on_task", "The bug does not belong to this task.");

        if (bug.Status != BugStatus.Open)
        {
            throw new DomainException("timelog.bug_not_open", $"Corrective hours need an Open bug; this bug is {bug.Status}.");
        }

        if (workDate < bug.FoundDate)
        {
            throw new DomainException("timelog.before_bug_found", $"Corrective work cannot be dated before the bug was found ({bug.FoundDate:yyyy-MM-dd}).");
        }

        if (Status is not (WorkTaskStatus.Reopened or WorkTaskStatus.UnderFix))
        {
            throw new DomainException("timelog.corrective_not_allowed", $"Corrective hours can only be logged on a Reopened or UnderFix task; this task is {Status}.");
        }

        var log = TimeLog.CreateCorrective(Id, bugId, employeeId, workDate, hours, note, hoursAlreadyLoggedThatDay, utcNow);
        if (Status == WorkTaskStatus.Reopened)
        {
            Transition(WorkTaskStatus.UnderFix, TransitionTrigger.CorrectiveWorkStarted, employeeId, "First corrective hours logged", utcNow);
        }

        return log;
    }

    /// <summary>
    /// Logs paid enhancement hours on a change request task while the request is Approved or InProgress. These hours
    /// are paid as normal work and never feed deductions.
    /// </summary>
    public TimeLog LogEnhancementHours(ChangeRequest changeRequest, int employeeId, DateOnly workDate, decimal hours, string? note, decimal hoursAlreadyLoggedThatDay, DateTime utcNow)
    {
        if (ChangeRequestId is null)
        {
            throw new DomainException("timelog.enhancement_not_allowed", "Enhancement hours can only be logged on a change request task.");
        }

        if (changeRequest.Id != ChangeRequestId)
        {
            throw new ArgumentException("The change request is not the one this task belongs to.", nameof(changeRequest));
        }

        EnsureAssigned(employeeId);
        if (!changeRequest.AcceptsEnhancementHours)
        {
            throw new DomainException("timelog.change_request_not_open", $"Enhancement hours need an Approved or InProgress change request; this one is {changeRequest.Status}.");
        }

        return TimeLog.CreateEnhancement(Id, employeeId, workDate, hours, note, hoursAlreadyLoggedThatDay, utcNow);
    }

    private void EnsureNotChangeRequest(WorkType workType)
    {
        if (ChangeRequestId is not null)
        {
            throw new DomainException("timelog.change_request_task", $"{workType} hours cannot be logged on a change request task; log Enhancement hours.");
        }
    }

    private void EnsureAssigned(int employeeId)
    {
        if (!IsAssignedTo(employeeId))
        {
            throw new DomainForbiddenException("timelog.not_assigned", "You can only log time on tasks assigned to you.");
        }
    }

    private TaskStateHistory Transition(WorkTaskStatus target, TransitionTrigger trigger, int? actorId, string? reason, DateTime utcNow)
    {
        TaskStateMachine.EnsureAllowed(Status, target, trigger);

        var history = new TaskStateHistory(Status, target, actorId, utcNow, reason);
        Status = target;

        if (target == WorkTaskStatus.Completed && ActualCompletionDate is null)
        {
            ActualCompletionDate = WorkTimeZone.ToWorkDate(utcNow);
        }

        if (target == WorkTaskStatus.Reopened)
        {
            ReopenCount++;
        }

        _history.Add(history);
        return history;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
