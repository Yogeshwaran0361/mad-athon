# Data model

MySQL 8, schema owned by EF Core migrations (`backend/src/ReworkGuard.Infrastructure/Persistence/Migrations`). Hangfire creates its own `Hangfire_*` tables at first start (see [OPERATIONS.md](OPERATIONS.md)). Shares are `decimal(5,2)` and hours `decimal(6,2)`; months are `char(7)` `yyyy-MM`; every timestamp is UTC.

```mermaid
erDiagram
    Project ||--o{ Module : has
    Module ||--o{ WorkTask : has
    WorkTask ||--o{ TaskAssignment : "assigned to"
    Employee ||--o{ TaskAssignment : works
    WorkTask ||--o{ TaskStateHistory : "append-only"
    WorkTask ||--o{ Bug : "reported on"
    Bug |o--o| Bug : "regression of"
    WorkTask ||--o{ TimeLog : "logged on"
    Employee ||--o{ TimeLog : logs
    Bug |o--o{ TimeLog : "corrective for"
    Bug ||--o{ AttributionShareOverride : "manager split"
    Employee ||--o{ EmployeeRateHistory : "pay over time"
    Employee |o--o{ Employee : "reports to"
    Bug ||--o{ Deduction : raises
    TimeLog ||--o{ Deduction : "priced from"
    Employee ||--o{ Deduction : "charged to"
    Deduction |o--o| Deduction : "reverses (compensating)"
    Deduction ||--o| DeductionDispute : "disputed by"
    PayrollPeriod ||--o{ PayrollRun : "runs for month"
    PayrollRun ||--o{ PayrollRunEmployee : "append-only"
    PayrollRunEmployee ||--o{ PayrollRunItem : "append-only"
    Deduction ||--o{ PayrollRunItem : "allocated in"
    Employee ||--o{ Notification : receives
    Notification ||--o{ OutboxMessage : "delivered via"
    Employee ||--o{ NotificationPreference : sets
    Employee ||--o{ RefreshToken : "sessions"

    Project { int Id PK; string Name; int WarrantyDays }
    Module { int Id PK; int ProjectId FK; string Name }
    WorkTask { int Id PK; int ModuleId FK; string Title; enum Status; date CommittedDate; date ActualCompletionDate; int ReopenCount; int Version }
    TaskAssignment { int Id PK; int TaskId FK; int EmployeeId FK; bool IsOriginalDeliverer }
    TaskStateHistory { long Id PK; int TaskId FK; enum FromStatus; enum ToStatus; int ChangedByEmployeeId; datetime ChangedAtUtc; string Reason }
    Bug { int Id PK; int TaskId FK; enum Severity; date FoundDate; bool WithinWarranty; enum RootCause; bool CausedByOriginalDelivery; enum Status; int RegressionOfBugId FK; int Version }
    TimeLog { int Id PK; int TaskId FK; int EmployeeId FK; date WorkDate; decimal Hours; enum WorkType; int BugId FK; enum ApprovalStatus; int ApprovedByEmployeeId; int Version }
    AttributionShareOverride { int Id PK; int BugId FK; int EmployeeId FK; decimal SharePercent; string Justification }
    Employee { int Id PK; string Code UK; string Email UK; string PasswordHash; enum Role; enum PayMode; decimal MonthlyGross; decimal DailyRate; decimal HourlyRate; int ManagerId FK; bool IsActive; int FailedLoginCount; datetime LockoutEndsAtUtc }
    EmployeeRateHistory { int Id PK; int EmployeeId FK; date EffectiveFrom; enum PayMode; decimal MonthlyGross; decimal DailyRate; decimal HourlyRate }
    Deduction { int Id PK; enum Kind; int EmployeeId FK; int BugId FK; int TimeLogId FK; char SourceMonth; char ApplyMonth; decimal Hours; decimal HourlyRateUsed; decimal SharePercent; decimal Amount; enum Status; int ReversesDeductionId FK; datetime DisputeWindowEndsAtUtc; datetime AcknowledgedAtUtc; int PayrollRunId; int Version }
    DeductionDispute { int Id PK; int DeductionId FK; string Reason; enum Outcome; string ResolutionJustification; int ReplacementDeductionId; int Version }
    PayrollPeriod { char Month PK; bool IsLocked; int FinalizedRunId; int Version }
    PayrollRun { int Id PK; char PeriodMonth FK; int RunVersion; enum Status; char InputsHash; char ApprovedInputsHash; decimal CapPercent; decimal TotalApplied; decimal TotalCarriedForward; decimal TotalRefunds; json ReconciliationJson; int Version }
    PayrollRunEmployee { long Id PK; int PayrollRunId FK; int EmployeeId FK; decimal SalaryBase; decimal CapAmount; decimal CarriedIn; decimal CurrentDue; decimal Refunds; decimal Applied; decimal CarriedForward; decimal SettledManually; decimal NetAdjustment }
    PayrollRunItem { long Id PK; long PayrollRunEmployeeId FK; int DeductionId FK; enum Kind; char OriginalApplyMonth; decimal Due; decimal Applied; decimal CarriedForward }
    AuditEvent { long Id PK; long Sequence UK; datetime OccurredAtUtc; int ActorEmployeeId; string EntityType; string EntityId; string Action; json DetailsJson; char PrevHash; char Hash }
    AuditChainHead { int Id PK; long Sequence; char Hash }
    Notification { long Id PK; int RecipientEmployeeId FK; enum Type; string Title; string Body; string Link; datetime ReadAtUtc }
    OutboxMessage { long Id PK; long NotificationId FK; enum Channel; enum Status; int Attempts; datetime NextAttemptAtUtc; string LastError; int Version }
    NotificationPreference { int EmployeeId PK; enum Type PK; bool EmailEnabled }
    RefreshToken { long Id PK; int EmployeeId FK; char TokenHash UK; guid FamilyId; datetime ExpiresAtUtc; datetime RevokedAtUtc; long ReplacedByTokenId }
    PolicySetting { string Key PK; string Value }
    Holiday { int Id PK; date Date UK; string Name }
```

## Database-enforced invariants

| Table | Guard |
| --- | --- |
| `AuditEvents` | `TR_AuditEvents_Chain` (BEFORE INSERT) assigns `Sequence`, `PrevHash`, `Hash` under a lock on `AuditChainHead`; no-update/no-delete triggers; the app user has only SELECT, INSERT |
| `TaskStateHistory` | No-update/no-delete triggers; SELECT, INSERT only |
| `PayrollRunEmployees`, `PayrollRunItems` | Triggers reject changes once the run is Finalized/Locked (`SQLSTATE 45000`); SELECT, INSERT only |
| `Deductions` | Check constraints: hours > 0, share in (0, 100], a Standard line is ≥ 0 with no reversed line, a Compensating line is ≤ 0 and names the line it reverses; a line can be reversed only once (unique `ReversesDeductionId`) |
| `PayrollPeriods` | One row per month; finalize takes `SELECT … FOR UPDATE` on it |
| Every mutable aggregate | `Version` concurrency token (MySQL has no `rowversion`); a stale write is `409 concurrency` |

Grants for the application user: [`scripts/db/app-user-grants.sql`](../scripts/db/app-user-grants.sql).
