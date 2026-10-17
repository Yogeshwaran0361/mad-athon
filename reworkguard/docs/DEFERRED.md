# Deferred work, deviations and assumptions

Everything the brief asked for that was not built, built differently, or rests on an assumption, with the reason. Legal and HR review items are in [BUSINESS_RULES.md](BUSINESS_RULES.md#for-hr-and-legal-review-separately).

> The deduction rules must be reviewed by HR and legal against wage-payment law (in India, the Payment of Wages Act, 1936 / Code on Wages, 2019) before production use. This is not legal advice.

## Not built (deferred)

| Item | Reason / extension point |
| --- | --- |
| Infrastructure as code (Terraform/CDK) | `docs/AWS_DEPLOYMENT.md` is a checklist plus ECS task definitions and `deploy.sh`. **Nothing was provisioned or executed on AWS**; the sandbox has no AWS account. |
| SignalR Redis backplane | Run one API task (or sticky sessions) until it is added. |
| Persisted Data Protection key ring | Keys are per container; only the Hangfire dashboard cookie depends on them. |
| WAF rules, Container Insights alarms, ECR lifecycle policies | Described in the AWS guide, not automated. |
| MFA, SSO, self-service password reset by email | Admins reset passwords; every reset revokes sessions. |
| Individual access-token revocation | See decisions below. |
| UI for: creating tasks, project/module management, unlocking accounts, `compensate` and `recalculate` | APIs exist and are tested (`POST /api/v1/tasks`, `/admin/projects`, `/admin/employees/{id}/unlock`, `/deductions/{id}/compensate`, `/bugs/{id}/deductions/recalculate`). |
| Manager picked by name in the employee form | The form takes the manager's employee id. |
| Board drag and drop with a library | Native HTML5 drag plus a keyboard-accessible status menu instead of dnd-kit; the board shows the first 100 tasks matching the filter. |
| Text alternative for dashboard charts | Charts have titles and the same figures are in Reports (table and downloads); no per-chart data table. |
| E2E in CI, other browsers | Playwright runs locally against compose (`docs/E2E.md`), Chromium only. CI runs the compose smoke test with `seed-demo`. |
| Incremental audit verification | `/audit/verify` re-hashes the whole chain every call; see below. |
| Statutory deductions and net pay | Out of scope: ReworkGuard produces the rework adjustment for the payroll system of record. |
| API image base OS patches in this sandbox | `backend/Dockerfile` runs `apt-get upgrade` (`APPLY_OS_UPDATES=true`, the default and what CI uses). The sandbox cannot reach deb.debian.org, so the locally scanned image still had 7 perl-base findings; see SECURITY.md. |

## Phase B decisions and deviations

- **Payroll run finalize vs. lock** (slice 5): Finalize moves lines Approved → Locked; Lock moves them Locked → Applied and publishes payslips. The brief has Finalize move lines to Applied. Reason: payslips and reconciliation are produced at Finalize while the run can still be inspected before it becomes irreversible.
- **Deduction lines are per corrective time log per deliverer**, not one line per bug. Reason: each line snapshots the hours, the pay rate on that work date and the share, so a later rate change or attribution override can be traced line by line.
- **In-app notifications go through the outbox too.** A live push therefore arrives when the outbox job next runs (the outbox job runs every 15 seconds), not instantly. Reason: nothing is pushed for a transaction that later rolls back.
- **Formal notices always go by email.** Employees cannot turn off email for DeductionNotified, DisputeResolved and PayslipPublished; every other type can be silenced by email. In-app notifications cannot be turned off (they are the employee's record).
- **SignalR runs in-process without a backplane.** With more than one API instance, add the Redis backplane (ElastiCache) or sticky sessions; documented in OPERATIONS.md.
- **SignalR access token in the query string** (`/hubs/...?access_token=`) because browsers cannot set headers on WebSockets. Request logging records the path only, so the token never reaches the logs.
- **Test-only SignalR client pinned to 8.0.32**, released three days before it was adopted, because every earlier 8.0.x carries advisory GHSA-334f-vvp4-7g6p. The same fix must be on the server: run on ASP.NET Core runtime 8.0.32 or later (the Docker image tag in slice 13 pins this).
- **Hangfire tables are created by Hangfire itself** (`Hangfire_*`, `PrepareSchemaIfNecessary`), not by EF migrations. The database user therefore needs CREATE on first start; OPERATIONS.md covers creating them with a privileged user and running the app with a narrower one.
- **Hangfire dashboard sign-in is a separate 30-minute cookie** issued by `POST /api/v1/admin/jobs/dashboard-session` (Admin only), because the API uses bearer tokens that a page navigation cannot send. Every dashboard request re-checks that the holder is still an active Admin. The cookie is encrypted with ASP.NET Core Data Protection; with more than one API instance the key ring must be shared (e.g. persisted to S3 or the database), documented in OPERATIONS.md.
- **Scheduled jobs are not retried by Hangfire.** They are idempotent and run again on the next schedule; overlapping runs are blocked by a distributed lock.
- **Swept tasks reject later bug reports.** After the warranty sweep closes a task, a bug report is refused as "raise it as a change request" (the Phase A rule for closed tasks outside warranty), so late defects never create deductions.
- **Library pins for advisories:** Newtonsoft.Json 13.0.3 and System.Data.SqlClient 4.8.6 are referenced directly because Hangfire.MySqlStorage 2.0.3 (via Dapper 1.50) would otherwise resolve versions with published advisories.
- **Payroll run states.** Preview → Reviewed (Finance) → Approved (a Manager who neither generated nor reviewed it) → Finalized (Finance: inputs re-checked against the approved hash, six reconciliation checks, lines move to `Locked`, the month is locked so later lines route to the next month) → Locked (Finance: lines move to `Applied`, compensated originals to `Reversed`). Regenerating supersedes any open run with RunVersion + 1. Months run strictly in order, up to the month after the current IST month, and a month cannot be finalized until the previous run is Locked.
- **A line's `Applied` status means "taken into a locked run".** Any part held back by the cap stays on that run's item as carried-forward and is collected by later runs; the line row itself is not changed again (it is immutable once finalized).
- **Refunds are uncapped and first cancel outstanding carry-forward** of the line they reverse; only the remainder is paid back. Changes to a line while it is `Locked` (finalized, run not yet locked) are held back and re-synced when the run locks, which raises compensating lines in the next open month.
- **No proration.** Monthly staff use the monthly gross in force on the 1st of the pay month (a mid-month change takes effect the following month); hourly and daily staff use approved hours actually worked in the pay month, so an hourly employee with no hours that month has a cap of 0 and everything carries forward.
- **Statutory deductions (PF, ESI, TDS, professional tax) and net pay are not modelled.** The run produces the rework adjustment for each employee, not a full payslip net; the payroll system of record applies it.
- **Leavers are settled manually.** An inactive employee with outstanding dues is included with cap 0 and the whole amount shown as `SettledManually` for HR to recover through full-and-final settlement. Whether recovery from final dues is lawful is an HR/legal question (see the legal note).
- **Access tokens live 15 minutes and are not revoked individually.** Deactivation, role changes and password resets revoke every refresh token at once, but an access token already issued stays valid until it expires. A deny-list would close that gap at the cost of a lookup per request.
- **Refresh tokens rotate with no grace period.** Two tabs refreshing with the same cookie at the same moment are treated as reuse and both sessions end; the frontend shares one refresh per tab to make this rare.
- **A locked account gets the same "Invalid email or password" answer** as a wrong password, so the lockout does not confirm the account exists. The user learns about it from an admin (who can unlock early) or by waiting 15 minutes.
- **Forwarded headers are trusted only from `ForwardedHeaders:KnownNetworks`.** Behind nginx or an ALB that list must name the proxy subnet, otherwise every client shares one rate-limit bucket.
- **`/audit/verify` re-hashes the whole chain on every call** (about 0.85 s for 71k entries on the shared 4-vCPU test VM, which works out to roughly 2 s at around 165k entries). Verifying incrementally from a recorded checkpoint is deferred because it changes what the endpoint attests. See PERFORMANCE.md.
- **Payroll run saves fetch each new payslip id in its own round trip** (200 for 200 employees, inside the 2 s budget); batch it before running thousands of employees.

## Phase A decisions still in force

1. **Data access uses `IApplicationDbContext` instead of per-entity repositories.** Application services query EF Core `DbSet`s through an interface; Infrastructure implements it. This kept the services small and the aggregate loading explicit. Application therefore references `Microsoft.EntityFrameworkCore` (abstractions only, no provider).
2. **Extra table `AttributionShareOverrides`.** The data model had nowhere to store a manager's share override. Computed shares are not stored; they are recalculated from approved regular hours whenever there is no override.
3. **Extra columns:** `Employee.PasswordHash` (auth), `Bug.CreatedAtUtc`, `TimeLog.CreatedAtUtc`, and an `int Version` concurrency column on Task, Bug, TimeLog and Deduction. MySQL has no `rowversion`, so `Version` is an EF concurrency token incremented on every update.
4. **Finance role added to the seed** (`finance@reworkguard.local`) so the Finance preview path can be exercised. The brief listed one Admin, Manager, TeamLead, QA and three Employees; all of those are present.
5. **Bug status endpoints** (`/verify`, `/close`) were added because closing a task requires every bug to be verified. Completing a fix (`UnderFix → Completed`) marks the task's open bugs `Fixed`.
6. **Approvers are TeamLead and Manager.** The brief names TeamLead; Manager is also allowed so a team lead's own corrective hours can be approved by someone.
7. **Regular hours also go through approval.** Attribution needs *approved* regular hours, so regular logs are `Pending` until a lead approves them, using the same queue.

## Assumptions to confirm

1. **Who is deducted, and at whose rate.** All approved corrective hours on a deductible bug (whoever logged them) are charged to the task's original deliverers by share, each at their own hourly rate on the work date.
2. **Hourly rate is rounded to 2 decimals before multiplying**, so every line can be recomputed by hand.
3. **Warranty window** is inclusive: `FoundDate <= ActualCompletionDate + WarrantyDays`; the first completion date never moves.
4. **Closing a task** needs every bug Verified/Closed, plus an expired warranty or an explicit Manager close.
5. **Bugs on Reopened/UnderFix tasks are rejected**; report a second defect after the current fix is completed.
6. **Daily 24 h limit** counts Pending and Approved logs.
7. **Work dates and months are in Asia/Kolkata**; timestamps are stored in UTC; the dispute window counts Mon–Fri working days minus configured holidays.
8. **20% cap against gross, 3-working-day dispute window, QA-miss not deductible, no proration** (defaults, all configurable except proration).
9. **Payroll runs one month at a time in order**, at most one month ahead of the current IST month.

## Enhancement requests and profit share: out of scope

Increment points and proposals; holdback and warranty-based release; invoices and cost tables; bug-to-CR conversion and reclassification; notifications; Hangfire jobs; dashboards and reports; repeat-customer points; caps and budgets; E2E tests.
