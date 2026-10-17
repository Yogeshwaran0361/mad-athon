using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Application.TimeLogs;

public sealed record TimeLogDto(
    int Id,
    int TaskId,
    string TaskTitle,
    WorkTaskStatus TaskStatus,
    int EmployeeId,
    string EmployeeName,
    DateOnly WorkDate,
    decimal Hours,
    WorkType WorkType,
    int? BugId,
    ApprovalStatus ApprovalStatus,
    int? ApprovedByEmployeeId,
    DateTime? ApprovedAtUtc,
    string? Note,
    int Version);
