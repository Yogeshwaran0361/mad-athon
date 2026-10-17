using ReworkGuard.Application.Tasks;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Application.Bugs;

public sealed record BugDto(
    int Id,
    int TaskId,
    string TaskTitle,
    WorkTaskStatus TaskStatus,
    BugSeverity Severity,
    string Description,
    DateOnly FoundDate,
    int ReportedByEmployeeId,
    BugStatus Status,
    bool WithinWarranty,
    RootCause? RootCause,
    bool? CausedByOriginalDelivery,
    int? ReviewedByEmployeeId,
    string? ReviewJustification,
    DateTime? ReviewedAtUtc,
    int? RegressionOfBugId,
    bool IsDeductible,
    int Version);

public sealed record BugReportedDto(
    BugDto Bug,
    DateOnly TaskCompletionDate,
    DateOnly WarrantyEndsOn,
    int ReopenCount,
    IReadOnlyList<AssigneeDto> OriginalDeliverers);
