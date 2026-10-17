# ReworkGuard Phase B report

Final head: the commit that adds this report (code verified at `750acf0`; this commit adds only this file) on `main` (local only; **not pushed**, as instructed in the thread). Source archive: `/mnt/project-files/downloads/reworkguard.zip` (git archive of that commit).

## (a) What was built, per slice

| Slice | Commit | Built |
| --- | --- | --- |
| 0 Audit | `a5992e3` | Phase A audit (`docs/PHASE_A_AUDIT.md`), Phase B plan, Testcontainers upgrade to clear SSH.NET advisories |
| 1 Lifecycle and disputes | `7fab7b7` | Deduction lines Draft → Notified → Disputed/Resolved → Approved → Locked → Applied, Cancelled/Reversed; disputes with uphold (cancel or smaller replacement) / reject; compensating lines; IST working-day dispute windows; acknowledgement |
| 2 Policy | `4d44f5b` | Policy admin with validation and audit, pay-rate history with effective dates, holiday calendar, IST work dates, QA-miss deductibility and share, regression attribution |
| 3 Notifications | `2d87671` | Transactional outbox, in-app notifications over SignalR, email via log/SMTP/SES, templates (Indian number grouping), per-type email preferences with mandatory formal notices |
| 4 Hangfire | `f885a50` | Hangfire on MySQL: outbox delivery (15 s), dispute-window reminders and expiry (5 min), warranty sweep (01:30), daily reminders (09:00 Mon–Fri IST); distributed locks; Admin job list/trigger; dashboard behind an Admin cookie; readiness checks |
| 5 Payroll run engine | `c40c2b4` | Versioned runs per month: generate → review → approve (four eyes) → finalize (inputs hash re-check, 6 reconciliation checks, month lock) → lock (lines applied, payslips published); cap per pay mode, carry-forward, uncapped refunds cancelling carry first, manual settlement for leavers; strict month order; locked-month routing; row-level month lock; DB triggers on finalized rows |
| 6 Payslips | `c96ddf6` | Payslip list/detail per locked run, scoped to self or Manager/Finance/Admin, QuestPDF statement, audited downloads |
| 7 Audit | `8bff74d` | SHA-256 hash chain computed by a MySQL trigger, backfill migration, `/audit/verify` (first break with reason), search filters, CSV export (formula-safe, capped, audited), least-privilege grants script revoking UPDATE/DELETE on history |
| 8 Reports | `9ffa208` | Rework summary, by module, by employee, payroll register in SQL with role scoping; JSON/CSV/Excel (ClosedXML)/PDF |
| 9 Frontend | `8c24b71` | Full MUI app: dashboard (recharts), board, tasks, approvals, bug review, deduction queue, my deductions, preview, payroll runs, payslips, notifications (SignalR), reports, audit, admin employees/settings/operations |
| 10 Security | `85accdd` | Lockout (5/15 min), hashed rotating refresh tokens with family revocation on reuse, HttpOnly cookie, 15-min access tokens, session revocation on deactivate/role/password change, security headers and CSP, Serilog JSON with correlation id, trusted forwarded headers, IDOR tests, generated authorization matrix; frontend silent refresh |
| 11 Performance | `f7fe498` | Deterministic volume seeder (200 employees, 5k tasks, 20k logs, 24 months, 71k audit rows, 23 real payroll runs), opt-in perf test (p95 ≤ 2 s), three indexes, faster `/audit/verify`, `docs/PERFORMANCE.md` with EXPLAIN ANALYZE |
| 12 E2E | `cacdbf8` | Playwright journeys (employee dispute, manager/finance payroll, admin/audit) with axe WCAG 2.1 AA scans; a11y fixes |
| 13 DevOps | `1da4615` | Multi-stage non-root Dockerfiles (API, migrator bundle, nginx web), compose with MySQL and MailHog, prod compose example, CI (build, drift, tests, coverage, vulnerable packages, npm audit, gitleaks, Trivy, compose smoke with seed), CodeQL, ECS CD workflow and `deploy.sh`, AWS guide, `scripts/seed-demo.sh/.ps1` (Ravi Login Module story), Development-only clock control |
| 14 Docs | `750acf0` | README, ARCHITECTURE, ER_DIAGRAM, BUSINESS_RULES (legal note), API, OPERATIONS, SECURITY, USER_GUIDE, DEMO_SCRIPT, DEFERRED, PERFORMANCE |

## (b) Commands run on the final head

Run in a clean clone (`git clean -fdx`) of `750acf0`:

| Command | Result |
| --- | --- |
| `dotnet build -warnaserror` | Pass, 0 warnings |
| `dotnet ef migrations has-pending-model-changes` | Pass, model matches migrations |
| `dotnet test --settings coverlet.runsettings --collect:"XPlat Code Coverage"` | Pass: 741 passed, 0 failed, 1 skipped (opt-in perf test), 12 m 26 s |
| `npm run lint` (frontend) | Pass |
| `npx tsc -b` (frontend) | Pass |
| `npx vitest run` (frontend) | Pass: 35 tests in 11 files |
| `gitleaks git` / `gitleaks dir` | No leaks |
| `dotnet list package --vulnerable --include-transitive` | No vulnerable packages |
| `npm audit` (frontend `--omit=dev`, e2e) | 0 vulnerabilities |
| Trivy on freshly built images | Web clean; API 7 perl-base findings (see (e)) |

Earlier, per-slice and per-branch (each commit was green when made, see (f) for the one exception):

| Command | Where | Result |
| --- | --- | --- |
| `docker compose up --build`, `curl /health/ready`, `scripts/seed-demo.sh` and `.ps1` end to end | slice 13 branch | Pass (story completes to a locked payroll run) |
| `npm run e2e` (Playwright, 4 tests, axe) | merged main `cacdbf8` | 4 passed (1.6 min) |
| Perf test at full volume and at 0.25 scale | slice 11 branch | Pass, all p95 ≤ 2 s |
| `deploy.sh` with `RENDER_ONLY=true` | main | Task definitions render and parse |
| actionlint on workflows | slice 13 branch | Pass |
| AWS provisioning / `deploy.sh` against AWS | — | **Not run**: no AWS account in the sandbox; documented only |

## (c) Tests and coverage

- Backend: 742 tests (741 run, 1 opt-in perf test skipped unless `RG_PERF=1`), domain unit tests plus Testcontainers MySQL integration tests.
- Frontend: 35 Vitest tests. E2E: 4 Playwright journeys with axe scans.
- Coverage (coverlet, line / branch, migrations excluded): **Domain 95.6% / 88.3%** (target 95%), **Application 98.6% / 82.8%** (target 85%), Infrastructure 96.4% / 83.0%, Api 96.8% / 86.4%, **overall 96.8% / 86.3%** (target 80%). All line-coverage targets met.

## (d) Performance (full volume, in-process API, tmpfs MySQL, 4 vCPU; details in PERFORMANCE.md)

| Endpoint | p50 ms | p95 ms |
| --- | ---: | ---: |
| Payroll generate (200 employees) | 342 | 344 |
| Task list / deep pages / board | 15–21 | 20–27 |
| Deduction lists / preview | 11–35 | 16–48 |
| Payroll run detail | 13–14 | 17–20 |
| Payslip list / PDF | 6–36 | 9–75 |
| Reports (24 months) | 65–78 | 87–108 |
| Payroll register JSON / xlsx | 12 / 114 | 16 / 268 |
| Audit list | 17–48 | 24–61 |
| Audit verify (71k entries) | 834 | 1,080 (was 5,791) |
| Notifications | 3–5 | 4–7 |

Caveat: no real network or disk sync; re-measure on production-like hardware.

## (e) Security scans

See [SECURITY.md](SECURITY.md#scan-results-run-on-this-commit): gitleaks clean (history and tree); no vulnerable NuGet packages; npm audit 0 (frontend, e2e); Trivy web image clean; Trivy API image 7 perl-base findings only because the sandbox build skipped `apt-get upgrade` (the default build applies the fix, and CI's Trivy gate enforces it); CodeQL configured but runs only on GitHub.

## (f) Gaps and deviations

- **Main was briefly red**: after merging slices 13 and 12 (`1da4615`, `cacdbf8`), five integration tests failed because sign-ins became audited in slice 10 and the matrix lacked the new admin endpoints. Fixed in `f7fe498`; slice 10 had been verified with targeted tests rather than the full suite.
- Not built, deviations and decisions: [DEFERRED.md](DEFERRED.md). Main ones: no IaC and nothing deployed to AWS; no SignalR backplane or shared Data Protection keys (single API instance); a few API-only actions without UI (create task, projects/modules, unlock, compensate, recalculate); E2E not in CI and Chromium only; finalize moves lines to Locked and lock moves them to Applied (brief had finalize → Applied); statutory deductions not modelled; `/audit/verify` re-hashes the whole chain (≈2 s at ~165k entries).
- CodeQL and the CD workflow have not run (repository not pushed).

## (g) Run locally

```bash
git clone <repo> reworkguard && cd reworkguard
./scripts/setup-dev.sh
docker compose up --build -d
./scripts/seed-demo.sh
```

Open http://localhost:8080 (password printed by `setup-dev.sh`); emails at http://localhost:8025.

## (h) Assumptions to confirm

1. All approved corrective hours on a deductible bug, whoever logged them, are charged to the original deliverers by attribution share at each deliverer's own rate on the work date.
2. Shares follow approved regular hours (equal split if none); managers may override with justification.
3. Cap 20% of the pay month's salary base: monthly gross on the 1st (no proration); hourly/daily staff by approved hours worked that month (so no hours means cap 0 and carry-forward).
4. Dispute window 3 working days (Mon–Fri IST minus holidays); a manager's decision is final in the system.
5. QA-miss bugs do not deduct by default.
6. Lines apply in the month after the work month, or the next open month if that one is locked; months run strictly in order.
7. Leavers' outstanding amounts are shown as manually settled for HR, not deducted.
8. ReworkGuard produces the rework adjustment only; the payroll system applies statutory deductions.

### Legal and HR review (separately)

The deduction rules must be reviewed by HR and legal against wage-payment law (in India, the Payment of Wages Act, 1936 and the Code on Wages, 2019) before production. This is not legal advice. Specifically:

1. Whether rework deductions are an authorised deduction under that law and the employment contracts, and whether written consent is needed.
2. The 20% cap and how it combines with other deductions under the statutory overall limit.
3. Whether the notice, dispute window and manager decision meet show-cause / hearing requirements.
4. Recovery from leavers' final settlement.
5. Retention of payslips, audit records and personal data (e.g. DPDP Act, 2023).
