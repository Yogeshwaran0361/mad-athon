# User guide

Sign in at the web address your administrator gives you. The menu on the left shows only what your role can use. The bell at the top shows notifications as they arrive; everything that needs you also goes to your email.

## Everyone

- **Tasks / Board**: tasks you are assigned to. Move a task from Assigned to In progress to Completed with the status button (or drag it on the board). Only a bug report can reopen a delivered task.
- **Log time** (task page → Log time): date, hours and whether it is *regular* work or *corrective* work on a bug. Corrective time must name the bug. You cannot log more than 24 hours on one day, and you cannot change the work type afterwards. Your lead approves each entry.
- **My deductions**: every deduction line raised against you, with the bug, hours, rate, your share and the amount.
  - **Notified** lines can be **disputed** within the dispute window (3 working days by default; the line shows the deadline). Give a reason; a manager decides and you are told the outcome by email.
  - **Acknowledge** tells the company you have seen the line. It does not stop you disputing it.
  - You get a reminder a day before a window closes.
- **Deduction preview**: what next month's payroll would take from you so far, with the cap applied and any amount carried forward.
- **Payslips**: one per locked payroll month that affected you; download the PDF.
- **Notifications** → preferences: turn off email for reminder-type messages. Formal notices (a new deduction, a dispute decision, a published payslip) are always emailed.

## QA

- Open a delivered task and **Report bug**: severity, description and the date found. The app tells you whether the bug is inside the warranty window. The task reopens.
- **Verify** a bug once the fix is confirmed.

## Team lead

- **Approvals**: approve or reject pending time entries from your team (not your own). Rejecting needs a reason, which the employee sees.
- Tasks are created with their deliverers through the API (`POST /api/v1/tasks`); there is no form for it yet. Close tasks whose warranty has expired once every bug is verified (the nightly sweep also does this).

## Manager

- **Bug review**: for each bug with corrective hours, record the root cause (own defect, new requirement, spec ambiguity, external issue, QA miss), whether it was caused by the original delivery, and a justification. Only an own defect from the original delivery, inside warranty, leads to deductions. You can override how the cost is split between deliverers, with a justification.
- **Deduction queue**: draft lines appear after your review. **Notify** sends them to the employees and starts the dispute window. **Approve** lines once the window has closed (or after resolving a dispute). Disputes show here: **Reject** (the line stands) or **Uphold** (the employee is right: the line is cancelled, or replaced by a smaller one if you enter fewer hours or a smaller share).
- **Payroll**: approve a reviewed run. You cannot approve a run you generated or reviewed.
- **Audit**: search the audit trail, **Verify chain** to check nobody has altered it, and export CSV.
- **Dashboard / Reports**: rework cost by month, module and employee; download as Excel, CSV or PDF.

## Finance

- **Payroll** → choose a month → **Generate**. Check the per-employee figures and the reconciliation, then **Review**. After a manager approves: **Finalize** (lines are frozen, the month is locked, payslips are prepared) and then **Lock** (lines are applied and payslips are published). If anything changed after approval, finalize refuses; regenerate the run.
- Months must be run in order. Late changes for a locked month automatically land in the next open month.
- **Payroll register** report: the figures to hand to the payroll system. Statutory deductions are not part of ReworkGuard.

## Admin

- **Employees**: create people, set role, manager and pay (monthly, daily or hourly, with an effective date), reset passwords, deactivate leavers. Deactivating or resetting a password signs the person out everywhere.
- **Settings**: policies (warranty days, cap %, dispute window, …), project warranty, holidays.
- **Operations**: background jobs (last and next run, run now), the email/notification outbox (retry failed messages) and the Hangfire dashboard.
- A locked account (5 wrong passwords) unlocks itself after 15 minutes; an admin can unlock it earlier through the API (`POST /api/v1/admin/employees/{id}/unlock`).
