# Phase B plan

Built in the slice order of the brief (section 8). Each slice ends with `dotnet build -warnaserror`, the full test suite, frontend checks when touched, and one commit.

## Key design decisions

1. **Deduction lines are per corrective time log per deliverer.** A `Deduction` row snapshots hours, the hourly rate valid on the work date (from `EmployeeRateHistory`), the share and the amount. A synchroniser keeps Draft lines in step with approvals, reviews, attribution overrides and dispute outcomes. Lines that are not yet finalized are cancelled and re-created; Locked/Applied lines are never edited and get a compensating (negative) line linked by `ReversesDeductionId`.
2. **Deduction state machine** in the Domain, same style as `TaskStateMachine`: `Draft → Notified → (Disputed → Resolved) → Approved → Locked → Applied`, plus `Cancelled` and `Reversed`. MySQL triggers reject changes to the money columns of Locked/Applied/Reversed rows, so immutability holds even outside the application.
3. **Work dates in Asia/Kolkata.** `IWorkCalendar` converts UTC instants to IST dates and computes working-day windows (weekends and the holiday calendar skipped). Storage stays UTC.
4. **Payroll run = pay month.** A run for `PeriodMonth` M takes every Approved, window-closed, not-yet-applied line with `ApplyMonth ≤ M`, plus carry-forward from the latest finalized run before M. The allocation engine is pure (`PayrollRunCalculator`, sharing a `CapAllocator` with the Phase A engine) and its canonical input is hashed (SHA-256) into `InputsHash`.
5. **Run workflow with two people.** Finance generates (Preview, new Version each time, older versions Superseded) and marks Reviewed; a Manager approves that version (second approver, cannot be the generator); Finance finalizes, which re-checks the `InputsHash` against the approved one, runs reconciliation, moves lines to Locked, writes payslips and locks the source month; Finance then locks the run, which moves lines to Applied and publishes payslips. Uniqueness on `(PeriodMonth, Version)` plus a row lock on a per-month guard row serialises generate/finalize.
6. **Notifications through an outbox.** Business transactions write `Notification` and `NotificationOutbox` rows only; a Hangfire job delivers email (SES or a dev file/SMTP sender) with retries and a dead-letter state. SignalR pushes in-app notifications to per-user groups.
7. **Hangfire on MySQL** (`Hangfire.MySqlStorage`). Jobs are thin wrappers over application services that are idempotent and tested directly. Dashboard behind an Admin-only authorization filter.
8. **Audit hash chain** written through a single serialised writer (an `AuditChainHead` row locked `FOR UPDATE` inside the business transaction).
9. **Reporting in SQL** (EF projections/grouped queries), tested against a seeded dataset with hand-calculated numbers.
10. **Frontend** stays on MUI; adds Recharts, `@microsoft/signalr`, `@dnd-kit` (keyboard-accessible drag) and Playwright + axe.

## Things that can only be written, not executed, in this environment

AWS deployment (ECS Fargate, RDS, Secrets Manager, SES, CloudWatch) and GitHub Actions runs. They are written, linted where tooling exists, and documented as unexecuted.
