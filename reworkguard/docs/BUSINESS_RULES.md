# Business rules

The rules ReworkGuard enforces, where each one lives in code, and the test that pins it. Policy values are read once per computation into a `PolicySnapshot` (`Domain/Policies/PolicySnapshot.cs`); a payroll run stores the snapshot it used, so a later policy change never alters a computed run.

> **Legal note.** Deducting money from wages is regulated. In India the Payment of Wages Act, 1936 (and the Code on Wages, 2019 once in force in the relevant state) limits which deductions are allowed, how much can be deducted in a wage period, and what notice and hearing the employee must get; other countries have equivalent laws. The rules below, including the 20% cap, the dispute window and recovery from leavers' final dues, are a configurable starting point, **not** a statement that they are lawful. HR and legal counsel must review them against the applicable law and the employment contracts before any deduction is applied to real pay. This document is not legal advice.

## Policy settings

Editable by an Admin (`PUT /api/v1/admin/policies`, audited). Defaults come from the seed migration.

| Key | Default | Meaning |
| --- | --- | --- |
| `StandardHoursPerMonth` | 160 | Divides a monthly gross into an hourly rate |
| `DailyHours` | 8 | Divides a daily rate into an hourly rate |
| `DeductionCapPercent` | 20 | Most that can be deducted in one pay month, as a % of that month's salary base |
| `DefaultWarrantyDays` | 45 | Warranty for projects that do not set their own |
| `DisputeWindowDays` | 3 | Working days (IST, Mon–Fri, minus holidays) an employee has to dispute a notified line |
| `QaMissDeductible` | false | Whether a `QaMiss` root cause can deduct at all |
| `QaMissSharePercent` | 50 | When it can, the share charged to the deliverers |
| `RequireEmployeeAcknowledgement` | false | When true, a payroll run cannot lock a standard line the employee has not acknowledged while its dispute window is still open |

All dates are in the work time zone, Asia/Kolkata (`Domain/Calendar/WorkTimeZone.cs`); timestamps are stored in UTC.

## Tasks

- **States**: Assigned → InProgress → Completed → Closed; Completed/Closed → Reopened (only by a bug report); Reopened → UnderFix (first corrective log); UnderFix → Completed. Anything else is `409 task.invalid_transition` (`Domain/Lifecycle/TaskStateMachine.cs`).
- Every transition writes a `TaskStateHistory` row and an audit event in the same transaction. `TaskStateHistory` is append-only (MySQL triggers plus the database grants).
- `ActualCompletionDate` is the first completion; completing a fix does not move it, so the warranty clock never restarts.
- Closing needs every bug Verified/Closed, plus either an expired warranty (TeamLead/Manager) or an explicit Manager close. The nightly **warranty sweep** closes tasks whose warranty has expired and have no open bugs.

## Bugs and warranty

- A bug is **within warranty** when `FoundDate ≤ ActualCompletionDate + WarrantyDays` (inclusive). Stored on the bug at report time.
- Reporting a bug reopens the task and increments `ReopenCount`. Bugs on Reopened/UnderFix tasks are rejected (report it after the current fix is completed). Bugs on a closed task outside warranty are refused as "raise a change request".
- A bug is **deductible** only when within warranty, the manager's review says it was caused by the original delivery, and the root cause is `OwnDefect` (or `QaMiss` when `QaMissDeductible` is on, with each deliverer's share scaled by `QaMissSharePercent`). `NewRequirement`, `SpecAmbiguity` and `ExternalIssue` never deduct (`Domain/Policies/DeductibilityPolicy.cs`).

## Time

- Regular hours only on Assigned/InProgress tasks; corrective hours only against an open bug on a Reopened/UnderFix task. The work type of a log never changes.
- At most 24 hours per employee per work date (Pending + Approved), enforced under a row lock on the employee.
- Every log needs approval by a TeamLead or Manager; nobody approves their own.

## Attribution

- The deliverers of a task share the cost of its rework by **approved regular hours**; with no regular hours the split is equal. Shares always total exactly 100.00 (the rounding remainder goes to the largest share) (`Domain/Attribution`).
- A Manager can override the split with a justification (`AttributionShareOverrides`, audited); a later override raises compensating lines.

## Deduction lines

- One line per approved corrective log per deliverer: `hours × hourly rate × share%`, rounded half away from zero to 2 decimals per line.
- Hourly rate by pay mode, on the work date: Monthly = gross ÷ `StandardHoursPerMonth`; Daily = daily rate ÷ `DailyHours`; Hourly = hourly rate. The rate is rounded to 2 decimals first, so every line can be recomputed by hand.
- Lines land in the **apply month**: the month after the work month, or the next open month if that one is already locked.
- **Lifecycle** (`Domain/Deductions`): Draft → Notified (the employee is told; the dispute window starts) → Disputed → decided by a Manager: **Rejected** (the line stands, Resolved) or **Upheld** (the line is cancelled; with new hours or share a smaller replacement line is raised in its place) → Approved (Manager; only after the window closes or the employee acknowledges) → Locked (payroll run finalized) → Applied (run locked). Cancelled and Reversed are terminal.
- When a fact changes later (hours re-approved, attribution override, review changed, pay rate corrected), the ledger re-syncs the bug (`Application/Deductions/DeductionLedger.cs`): lines not yet finalized are cancelled and re-drafted; a finalized line that no longer matches is reversed by a **compensating** line (`DeductionKind.Compensating`, the negative of the original) and a new standard line is raised for whatever is still due, so a deducted amount comes back as a refund in the next run. A line whose amount a manager set in a dispute is only cancelled if the bug stops being deductible.

## Payroll runs

- **States**: Preview → Reviewed (Finance) → Approved (a Manager who neither generated nor reviewed it) → Finalized (Finance) → Locked (Finance). Regenerating supersedes any open run with `RunVersion + 1` (`Application/Payroll/PayrollRunService.cs`).
- Months run strictly in order, up to the month after the current IST month. A month cannot be finalized until the previous month's run is Locked.
- **Finalize** re-computes the inputs and refuses if they changed since approval (`payroll.inputs_changed`), runs six reconciliation checks, moves lines Approved → Locked and locks the month, so new lines route to the next month.
- **Lock** moves lines Locked → Applied, publishes payslips and re-syncs the ledger, so a change made while lines were Locked becomes a compensating line in the next open month.
- **Cap**: per employee per month, `DeductionCapPercent`% of the salary base. Monthly staff: the gross in force on the 1st of the pay month (no proration). Hourly and daily staff: approved hours actually worked in the pay month × rate, so with no hours the cap is 0.
- **Allocation**: carried-in amounts first (oldest first), then current lines chronologically; whatever exceeds the cap carries forward to the next month.
- **Refunds** are uncapped and first cancel the outstanding carry-forward of the line they reverse; only the remainder is paid back.
- **Leavers**: an inactive employee with dues is included with cap 0 and the whole amount shown as `SettledManually`, for HR to handle in full-and-final settlement (see the legal note).
- Finalized/locked run rows (`PayrollRunEmployees`, `PayrollRunItems`) are append-only in the database.

## Notifications

- Every notification goes through the transactional outbox, so nothing is sent for a rolled-back change; the outbox job delivers every 15 seconds (in-app via SignalR, email via SMTP or SES).
- Formal notices (`DeductionNotified`, `DisputeResolved`, `PayslipPublished`) are always emailed; other email types can be turned off per employee. In-app notifications cannot be turned off.
- Reminders: at 09:00 IST Mon–Fri, approvers with pending logs and managers with draft lines. The deduction-window job (every 5 minutes) reminds employees whose dispute window closes within 24 hours and managers with disputes awaiting a decision.

## Audit

- Every state change writes an `AuditEvent` in the same transaction. Rows are hash-chained by a MySQL trigger (SHA-256 over sequence, previous hash, time, actor, entity, action and details) and cannot be updated or deleted by the application user. `GET /api/v1/audit/verify` recomputes the chain and reports the first break (see [SECURITY.md](SECURITY.md)).

## Assumptions to confirm with the business

1. All approved corrective hours on a deductible bug, whoever logged them, are charged to the original deliverers by share at each deliverer's own rate.
2. The cap applies to the rework deduction alone, against gross pay; statutory deductions (PF, ESI, TDS, professional tax) are not modelled.
3. Dispute window of 3 working days; a manager decides disputes and the decision is final inside the system.
4. No proration of the monthly gross for mid-month joins, leaves or pay changes.
5. QA-miss bugs do not deduct by default.

### For HR and legal review (separately)

1. Whether rework deductions are an authorised deduction under the Payment of Wages Act / Code on Wages (or local law) and the employment contracts, and whether written consent is needed.
2. The 20% monthly cap, and whether it must combine with other deductions under the statutory overall limit.
3. Whether the notice, 3-working-day dispute window and manager decision satisfy the "show cause" / hearing requirements.
4. Whether recovery from a leaver's final settlement (`SettledManually`) is lawful.
5. Retention period for payslips, audit records and personal data, and data-protection obligations (e.g. India's DPDP Act, 2023).
