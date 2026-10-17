# Phase A audit

Audited on 2026-10-16 before any Phase B work, from a fresh clone of the Phase A repository (11 commits, HEAD `d38ea40`).
Environment: .NET SDK 8.0.131, Node 22.22, Docker 29, MySQL 8.0 (`mysql:8.0` image).

## Commands run

| Command | Result |
| --- | --- |
| `dotnet build -warnaserror` | Pass, 0 warnings |
| `dotnet ef migrations has-pending-model-changes` | Pass, model and migrations in sync |
| `dotnet test` | Pass, 371 passed, 0 failed (297 unit, 74 Testcontainers MySQL integration) |
| `dotnet ef database update` on an empty MySQL 8 database | Pass: `InitialCreate`, `AppendOnlyGuards` applied; 12 tables and 4 append-only triggers created |
| `npm ci`, `npm run lint`, `npm run typecheck`, `npm test`, `npm run build` | Pass (14 Vitest tests) |
| `npm audit --omit=dev` and `npm audit` | 0 vulnerabilities |
| `dotnet list package --vulnerable --include-transitive` | **Fail**: `ReworkGuard.Tests` pulls `SSH.NET 2023.0.0` (2 High advisories) through `Testcontainers 3.10.0` |

## What works (verified against the Phase A spec)

- Layered solution (Domain, Application, Infrastructure, Api, Tests), EF Core 8 + Pomelo, FluentValidation, React 19 + Vite + TanStack Query + RHF/Zod + MUI, MySQL via Docker Compose, CI workflow.
- JWT login with all six roles; every controller action names its roles (reflection test).
- All spec entities exist with `decimal(12,2)` money, `decimal(6,2)` hours, UTC timestamps, `Version` concurrency tokens, months as `yyyy-MM`.
- Task state machine with append-only history (EF guard and MySQL triggers); bug reporting with auto-reopen, warranty check and original deliverers; time logging with Regular/Corrective tagging, the 24 h/day limit under a row lock, lead approval, no self-approval; manager defect review; attribution summing to exactly 100.00; pure deduction engine and `GET /api/v1/deductions/preview`.
- Minimal UI: login, task list, task detail with history, bug form, corrective time log form, approval queue, deduction preview.

## Defects found and fixed in slice 0

| # | Defect | Fix |
| --- | --- | --- |
| 1 | High-severity advisories (GHSA-q939-rpr3-3284, GHSA-mggc-4xg6-vcxf) in `SSH.NET 2023.0.0`, a transitive dependency of `Testcontainers.MySql 3.10.0`. Test-only, but the Phase B CI must fail on High findings. | Upgraded `Testcontainers.MySql` to 4.16.0 (pulls `SSH.NET 2024.2.0`, no advisories) and moved to the image-parameter `MySqlBuilder` constructor. Full suite re-run green. |

## Latent defects that block Phase B (fixed in the slice that needs them)

| # | Defect | Fixed in |
| --- | --- | --- |
| 2 | `AuditWriter` and `HttpCurrentUser` throw when there is no HTTP request, so background jobs could not write audit events. | Slice 3/4: a system actor for jobs. |
| 3 | "Today" checks (future work date, future found date, warranty expiry) use the UTC date. Between 00:00 and 05:30 IST a user logging today's hours is told the date is in the future. Phase B requires Asia/Kolkata work dates. | Slice 2: `IWorkCalendar` with IST dates. |
| 4 | Error responses carry no correlation id; logs are not structured. | Slice 10 (Serilog, correlation id). |
| 5 | Only `/health` exists; no readiness probe for DB/Hangfire. | Slice 4/13. |

## Deviations from the Phase A spec (kept, already documented in DEFERRED.md)

Data access via `IApplicationDbContext` rather than repositories; extra `AttributionShareOverrides` table; extra columns (`PasswordHash`, `CreatedAtUtc`, `Version`); Finance demo user; bug verify/close endpoints; Managers may approve hours; regular hours also need approval. None conflicts with Phase B, so none was rewritten.

One further observation: clients cannot send an expected `Version` (no `If-Match`), so the concurrency token protects concurrent requests but not a stale screen. Phase B adds expected-version checks on the new money-affecting commands (deduction transitions, payroll run steps).
