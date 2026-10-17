# ReworkGuard

Rework-aware task tracking and payroll deduction ("Pay-Once Ledger").

When a delivered module comes back with a defect inside its warranty window, the fix hours are tracked as **corrective** time, linked to the bug, the original task and its original deliverers, and kept strictly apart from regular work. Once a lead approves those hours and a manager confirms the defect was the deliverers' own, deduction lines are raised, the employees are notified and can dispute, and approved lines flow through a monthly payroll run (capped, with carry-forward) into payslips. Every step is in a hash-chained audit trail.

> **Legal note.** Wage deductions are regulated (in India: the Payment of Wages Act, 1936 and the Code on Wages, 2019). The rules here are configurable defaults and must be reviewed by HR and legal before production use. This is not legal advice. See [docs/BUSINESS_RULES.md](docs/BUSINESS_RULES.md).

## Run it locally (4 commands)

Prerequisites: Docker with Compose, bash, openssl, curl and jq.

```bash
git clone <this repo> reworkguard && cd reworkguard
./scripts/setup-dev.sh          # random local secrets into .env (git-ignored); prints the demo password
docker compose up --build -d    # MySQL 8, API, web (nginx) and MailHog; migrates and seeds demo users
./scripts/seed-demo.sh          # optional: the "Ravi Login Module" story through the API
```

Then open http://localhost:8080 (app), http://localhost:8025 (MailHog, every email sent) and http://localhost:5080/swagger (API).

Demo users share the `SEED_PASSWORD` from `.env`: `admin@`, `manager@`, `lead@`, `qa@`, `finance@`, `priya@`, `dev@`, `hana@reworkguard.local`. The seed story adds `ravi@` and `meera@` (see [docs/DEMO_SCRIPT.md](docs/DEMO_SCRIPT.md)). Reset with `docker compose down -v`.

For backend/frontend development without containers: `docker compose up -d mysql`, `dotnet run --project backend/src/ReworkGuard.Api` (port 5080) and `cd frontend && npm ci && npm run dev` (port 5173, proxies `/api`). Needs .NET 8 SDK and Node 22.

## What is in the box

| Area | Highlights |
| --- | --- |
| Tasks, bugs, time | Strict task state machine, warranty check at bug report, regular vs corrective hours, 24 h/day limit, approvals (no self-approval) |
| Deductions | Root-cause review, attribution by approved regular hours (manager override), per-log lines, notify → dispute window → decision → approve, compensating lines instead of edits |
| Payroll | Monthly runs in strict order: generate → review → approve (four eyes) → finalize (inputs re-checked, 6 reconciliation checks, month locked) → lock (payslips published); cap with carry-forward; refunds; manual settlement for leavers |
| Payslips | Per employee per locked month, PDF via QuestPDF |
| Notifications | Transactional outbox, in-app over SignalR, email via SMTP/SES, preferences, reminders |
| Jobs | Hangfire on MySQL: outbox delivery, dispute windows, warranty sweep, daily reminders; Admin operations page |
| Reports | Rework summary, by module, by employee, payroll register; JSON/CSV/Excel/PDF; role-scoped |
| Audit | Every change audited; SHA-256 hash chain built by a MySQL trigger; `/audit/verify`; CSV export; DB user cannot UPDATE/DELETE history |
| Security | Lockout, rotating refresh tokens with reuse detection, strict headers/CSP, correlation ids, JSON logs, IDOR tests, generated authorization matrix, gitleaks/CodeQL/Trivy/npm audit in CI |
| Frontend | React 19 + TypeScript + MUI: board, task detail, approvals, bug review, deduction queue, my deductions, payroll, payslips, dashboard, reports, audit, admin |

## Documentation

| Doc | For |
| --- | --- |
| [ARCHITECTURE](docs/ARCHITECTURE.md) | Layers, request flow, jobs, payroll engine, audit chain, decisions |
| [ER_DIAGRAM](docs/ER_DIAGRAM.md) | Data model and database-enforced invariants |
| [BUSINESS_RULES](docs/BUSINESS_RULES.md) | Every rule, the policies, assumptions, HR/legal review list |
| [API](docs/API.md) and [AUTHORIZATION_MATRIX](docs/AUTHORIZATION_MATRIX.md) | Conventions, errors, endpoints and roles |
| [USER_GUIDE](docs/USER_GUIDE.md) | What each role does in the app |
| [DEMO_SCRIPT](docs/DEMO_SCRIPT.md) | The Ravi Login Module walk-through |
| [OPERATIONS](docs/OPERATIONS.md) | Configuration, migrations, DB grants, monitoring, runbooks |
| [SECURITY](docs/SECURITY.md) | Threat model, controls, scan results |
| [PERFORMANCE](docs/PERFORMANCE.md) | Volume test, timings, EXPLAIN plans |
| [REPORTS](docs/REPORTS.md) | Report definitions |
| [E2E](docs/E2E.md) | Playwright journeys and accessibility scans |
| [AWS_DEPLOYMENT](docs/AWS_DEPLOYMENT.md) | ECS Fargate/RDS/SES reference (documented, not provisioned) |
| [DEFERRED](docs/DEFERRED.md) | What was not built, deviations, assumptions |

## Repository layout

```
backend/src/ReworkGuard.Domain          entities, state machine, policies, attribution, payroll engine (no I/O)
backend/src/ReworkGuard.Application     services, DTOs, validators, abstractions
backend/src/ReworkGuard.Infrastructure  EF Core + MySQL, migrations, triggers, Hangfire, email, PDF/Excel, JWT
backend/src/ReworkGuard.Api             controllers (/api/v1), auth, middleware, SignalR hub
backend/tests/ReworkGuard.Tests         xUnit unit + Testcontainers MySQL integration tests, perf test (RG_PERF=1)
frontend/                               React SPA (Vite, MUI, TanStack Query), Vitest
e2e/                                    Playwright journeys with axe
scripts/                                setup-dev, seed-demo (.sh/.ps1), verify, db grants
deploy/ecs/                             ECS task definitions and deploy.sh
.github/workflows/                      ci.yml, codeql.yml, cd.yml
```

## Tests and checks

```bash
./scripts/verify.sh                      # what CI runs: build -warnaserror, migration drift, dotnet test, frontend lint/typecheck/test/build
cd e2e && npm ci && npm run e2e          # Playwright against the compose stack
RG_PERF=1 dotnet test backend --filter Category=Performance   # volume test (RG_PERF_SCALE=1 for full volume)
```

Integration tests need Docker (Testcontainers starts MySQL 8). Coverage: `dotnet test backend --settings backend/coverlet.runsettings --collect:"XPlat Code Coverage"`.

## Configuration and secrets

Nothing secret is committed. Locally `setup-dev.sh` writes `.env` and .NET user-secrets; in production values come from environment variables backed by a secret store (AWS Secrets Manager in the reference deployment). The API refuses to start without `Jwt__SigningKey` (≥ 32 chars) and a connection string. Full list: [docs/OPERATIONS.md](docs/OPERATIONS.md).

Migrations: `dotnet ef migrations add <Name> -p backend/src/ReworkGuard.Infrastructure -s backend/src/ReworkGuard.Api -o Persistence/Migrations` (run `dotnet tool restore` in `backend/` first).
