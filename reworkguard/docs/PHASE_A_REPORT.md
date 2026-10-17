# ReworkGuard Phase A: build report

All results below come from commands run on 2026-10-16 in a Linux container with the .NET 8.0.131 SDK, Node 22.22 and Docker 29 running MySQL 8.0.46. The final verification ran against a fresh `git clone` of the repository.

## (a) What was built, per slice

| # | Commit | Delivered |
| --- | --- | --- |
| 1 | `8943f45` | Layered solution (Domain, Application, Infrastructure, Api, Tests) and `docker-compose.yml` for MySQL 8. EF Core 8 + Pomelo mapping of every entity in the brief, with the required CHECK constraints (hours 0–24, corrective needs a bug), unique (TaskId, EmployeeId), FKs, indexes on (EmployeeId, WorkDate), (TaskId) and status columns, and an `int Version` concurrency token on Task, Bug, TimeLog and Deduction. Two migrations: `InitialCreate` (which also seeds the policy defaults) and `AppendOnlyGuards` (MySQL triggers that reject UPDATE/DELETE on AuditEvents and TaskStateHistory). A dev seeder creates Admin, Manager, TeamLead, QA, Finance, 3 employees (Monthly, Daily, Hourly), 1 project, 2 modules and 4 tasks. |
| 2 | `7a49864` | JWT login (PBKDF2 hashing, rate limited at 10/min per IP, same 401 for an unknown email or a wrong password), signing key from config with startup validation, a fallback "authenticated" policy plus explicit role lists on every action, ProblemDetails mapping (400/401/403/404/409), the audit writer and a read-only audit endpoint. |
| 3 | `e122521` | `TaskStateMachine`, which lists the legal transitions and the trigger each one needs. `WorkTask` aggregate with append-only history and close guards: bugs must be verified, and the warranty must have expired unless a manager closes. Task API: paged/filtered list, detail with history, create, transition. Each transition writes history and an audit event in one transaction. |
| 4 | `7eca748` | Bug reporting on Completed tasks, or Closed tasks inside warranty. WithinWarranty is computed once and stored. The task is auto-reopened and ReopenCount incremented, and the original deliverers are returned. Outside-warranty bugs are stored but can never deduct. Verify and close endpoints complete the fix loop. |
| 5 | `28db629` | Time logging. Regular hours are accepted only on Assigned/InProgress tasks (409 otherwise). Corrective hours need an Open bug on a Reopened/UnderFix task, and the first one moves the task to UnderFix. Work type is fixed at creation. The 24 h/day limit is enforced under a per-employee row lock (proven with 4 concurrent requests). The approval queue hides the approver's own logs, and self-approval returns 403. Every decision is audited. |
| 6 | `6512ec1` | Manager-only root-cause review. A justification of at least 10 characters is required when the deliverer is cleared or the cause is not OwnDefect. Deductible only when within warranty, caused by the original delivery and OwnDefect. Attribution splits by approved regular hours (equal split if there are none) and always sums to 100.00, with the remainder going to the largest share. Managers can override the split with a justification, audited before and after. |
| 7 | `ae625e5` | Pure deduction engine: pay-mode rate conversion, per-line rounding half away from zero, total equal to the sum of the rounded lines, cap from `ISalaryBaseProvider` (gross by default), chronological allocation with partial-line carry-forward, and December→January apply month. `GET /api/v1/deductions/preview` chains carry-forward from the first month with deductible hours. |
| 8 | `2a06ef0` | React 19 + TS strict + Vite + MUI + TanStack Query + React Router + RHF/Zod. Screens: login, task list with status badges, task detail (state history, bugs, logs, allowed transitions), bug report form, corrective time log form, approval queue, deduction preview. A Playwright run against the live API covered QA reports a bug → employee logs corrective hours → lead approves → manager reviews → employee sees the preview (800.04 = 4 h × 300 × 66.67%). |
| 9 | `84c3f20` | GitHub Actions CI (backend build with `-warnaserror`, migration drift check, `ef database update` against a fresh MySQL service, tests; frontend lint, typecheck, tests, build), README, `docs/DEFERRED.md`, `scripts/verify.sh`. |
| – | `1dd4465` | Vitest 3 → 4 to clear two dev-only npm audit advisories (now 0). |

## (b) Commands run and results (fresh clone, final pass)

| Command | Result |
| --- | --- |
| `dotnet build -warnaserror` | Pass: 0 warnings, 0 errors |
| `dotnet ef migrations has-pending-model-changes` | Pass: "No changes have been made to the model since the last migration." |
| `dotnet test` | Pass: 371 passed, 0 failed, 0 skipped (≈1 min 7 s) |
| `dotnet ef database update` on a new empty MySQL database | Pass: applied `InitialCreate` and `AppendOnlyGuards`; 12 tables and 4 append-only triggers present |
| Dev startup (`dotnet run`, Development) | Pass: migrated, seeded, `/health` returns Healthy, Swagger 200 |
| `npm ci` | Pass |
| `npm run lint` (`eslint . --max-warnings 0`, typescript-eslint strict-type-checked) | Pass |
| `npm run typecheck` (`tsc --noEmit`) | Pass |
| `npm test` (Vitest) | Pass: 14 passed |
| `npm run build` | Pass |
| `npm audit` / `dotnet list package --vulnerable` | 0 vulnerabilities / no vulnerable packages |
| `docker compose config -q` and CI YAML parse | Pass |

CI itself has not run yet, because the repository is not on GitHub.

## (c) Test counts

- Backend total: **371** (297 domain unit tests, 74 MySQL-backed integration/API tests via Testcontainers).
  - State machine: every from × to × trigger combination (108 cases), illegal manual moves, close guards.
  - Warranty: the exact last day and the day after, closed-task rules, found-date guards.
  - Time: the 24 h boundary (exactly 24 allowed, 24.01 rejected), regular-on-reopened rejection, self-approval, decide-once, concurrent 24 h race.
  - Attribution: 3-way split sums to exactly 100.00, remainder placement, override validation, and a 2,000-case property check that shares always sum to 100.00.
  - Engine (50 tests): each PayMode conversion, .005 rounding boundaries, sum-of-rounded-lines, cap hit/not hit/exactly hit/zero, carry-forward chaining over 7 months, December→January, zero-hours/no-deduction, input guards, order independence, and a 1,000-case property check that Applied + CarryForward == Total and Applied ≤ Cap.
  - End to end over HTTP: bug → reopen → log → approve → review → preview, plus cap carry-forward into the next month, manager-cleared bug, override, and preview authorization.
- Frontend: **14** Vitest tests (bug form and time-log form validation, server error mapping).

## (d) Known gaps and deviations

See [DEFERRED.md](DEFERRED.md). In short:

- Data access goes through `IApplicationDbContext` (DbSets) instead of per-entity repositories.
- An extra `AttributionShareOverrides` table stores manual shares.
- Extra columns: `PasswordHash`, `CreatedAtUtc` on bugs and logs, and `Version` as the concurrency token (MySQL has no rowversion).
- A Finance demo user was added.
- Bug verify and close endpoints were added.
- Managers can approve hours as well as team leads.
- Regular hours also need approval, because attribution uses approved hours.
- No root-cause review UI (API only, as specified).
- No month-close run.

## (e) Run locally (4 commands)

```bash
./scripts/setup-dev.sh
docker compose up -d mysql
dotnet run --project backend/src/ReworkGuard.Api
cd frontend && npm ci && npm run dev
```

## (f) Assumptions to confirm

1. All approved corrective hours on a deductible bug, whoever logged them, are charged to the original deliverers by share, each at their own hourly rate.
2. The hourly rate is rounded to 2 decimals before multiplying, so every line can be recomputed by hand.
3. The warranty is inclusive (found date ≤ completion + warranty days) and measured from the first completion.
4. Bugs cannot be reported while a task is Reopened/UnderFix. Report the next one after the fix completes.
5. Carry-forward in the preview is replayed from the first month with deductible hours, since month-close results are not stored yet.
6. The daily 24 h limit counts Pending and Approved logs, not Rejected ones.
7. Daily and hourly staff are capped against a standard month (DailyRate × 20, HourlyRate × 160) until real earnings feed `ISalaryBaseProvider`.
