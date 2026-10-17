using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Application.Tasks;

public sealed record AssigneeDto(int EmployeeId, string Name, bool IsOriginalDeliverer);

public sealed record TaskSummaryDto(
    int Id,
    string Title,
    int ModuleId,
    string ModuleName,
    int ProjectId,
    string ProjectName,
    WorkTaskStatus Status,
    DateOnly CommittedDate,
    DateOnly? ActualCompletionDate,
    int ReopenCount,
    IReadOnlyList<AssigneeDto> Assignees);

public sealed record TaskHistoryDto(
    long Id,
    WorkTaskStatus FromStatus,
    WorkTaskStatus ToStatus,
    int? ChangedByEmployeeId,
    string ChangedByName,
    DateTime ChangedAtUtc,
    string? Reason);

public sealed record TaskBugDto(
    int Id,
    BugSeverity Severity,
    string Description,
    DateOnly FoundDate,
    BugStatus Status,
    bool WithinWarranty,
    RootCause? RootCause,
    bool? CausedByOriginalDelivery,
    bool IsReviewed,
    bool IsDeductible);

public sealed record TaskTimeLogDto(
    int Id,
    int EmployeeId,
    string EmployeeName,
    DateOnly WorkDate,
    decimal Hours,
    WorkType WorkType,
    int? BugId,
    ApprovalStatus ApprovalStatus,
    string? Note);

public sealed record TaskDetailDto(
    TaskSummaryDto Task,
    int WarrantyDays,
    DateOnly? WarrantyEndsOn,
    int Version,
    IReadOnlyList<WorkTaskStatus> AllowedTransitions,
    IReadOnlyList<TaskHistoryDto> History,
    IReadOnlyList<TaskBugDto> Bugs,
    IReadOnlyList<TaskTimeLogDto> TimeLogs,
    int? ChangeRequestId = null);
