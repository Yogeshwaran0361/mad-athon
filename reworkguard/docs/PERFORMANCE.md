# Performance

Target from the brief: with 200 employees, 5,000 tasks, 20,000 time logs and 24 months of history, the key endpoints
respond in under 2 s. **Result: every measured endpoint meets the budget at p95.** One query (`/audit/verify`) failed
before this slice at 4.4 s p50. It was rewritten and now takes 0.83 s. It is the only endpoint that grows with the
whole history (see [Limits](#limits-and-deferred-work)).

## Dataset

The seeder is `PerfSeeder` (`backend/tests/ReworkGuard.Tests/Support/Perf/`). It uses a fixed random seed (`20240401`),
so the same scale always produces the same data. History runs from 2024-04 to 2026-03, and the clock is
2026-03-28 09:00 UTC.

| Table | Rows (full scale) | How it is written |
|---|---:|---|
| Employees | 200 | EF and the domain model. 1 admin, 4 finance, 8 managers, 16 leads, 20 QA, 151 staff. Staff and QA pay: 60% monthly, 25% hourly, 15% daily |
| EmployeeRateHistory | 217 | Domain `ChangePay`. 30% of hourly and daily staff get a raise effective 2025-04-01 |
| Projects / Modules | 8 / 48 | EF. Warranty windows of 30 to 90 days |
| WorkTasks / TaskAssignments | 5,000 / 6,328 | Bulk multi-row INSERT. Spread evenly over 24 months. Statuses follow the lifecycle: Closed after warranty, Completed, Reopened/UnderFix while a bug is open, Assigned/InProgress for recent tasks |
| TaskStateHistory | 18,921 | Bulk. One row per transition, consistent with each task's status |
| TimeLogs | 20,000 | Bulk. 16,000 regular and 4,000 corrective logs, dated inside each task's (or bug's) window, at most 24 h per person per day. Older than 4 days: approved. Recent: a mix of approved, pending and rejected |
| Bugs | 1,500 | Bulk. Found inside the warranty window. 90% reviewed, with a realistic root-cause mix |
| Deductions | 2,315 | Drafted by the real `DeductionLedger` (attribution, rate history and policy). Then moved through the domain lifecycle: past months Notified→Approved (3% cancelled by decision); the open month a mix of Approved, Notified, Disputed and Draft |
| PayrollRuns / RunEmployees / RunItems | 23 / 4,600 / 3,703 | **Real API calls.** Each month from 2024-04 to 2026-02 runs generate → review → approve → finalize → lock, so every payslip is real |
| AuditEvents | 71,326 | Bulk INSERT in time order, **through the hash-chain trigger**. Domain and API events come on top |
| Notifications | 7,466 | Bulk. 30 per employee over the 24 months, 85% of older ones read |

All DB triggers (append-only tables, finalized-line immutability, audit chain) and check constraints stay on while
seeding. Seeding the full scale takes about 55 s.

Known users (password `Correct-Horse-9`): `perf.admin@`, `perf.manager1@`, `perf.manager2@`, `perf.finance1@`,
`perf.finance2@`, `perf.lead@`, `perf.employee@` (all `@perf.local`).

## Method

- The opt-in test `PerformanceTests` (trait `Category=Performance`) seeds a fresh migrated MySQL 8.0 database. The
  database runs in the Testcontainers fixture (tmpfs, `innodb_flush_log_at_trx_commit=0`, no binlog). The test hosts the
  real API with `WebApplicationFactory`, so requests go through the full pipeline (JWT auth, MVC, EF, serialisation) but
  over the in-memory test transport rather than a real network.
- Each endpoint gets 2 warm-up requests and then 20 timed sequential requests (`RG_PERF_ITERATIONS`). p50 and p95 are
  nearest-rank. Payroll generate gets 1 warm-up and 3 timed runs (`RG_PERF_GENERATE_RUNS`); each run supersedes the
  previous preview and recomputes the month for all 200 employees.
- The test asserts **p95 ≤ 2 s** for every endpoint.
- Before the timed section the test truncates `performance_schema.events_statements_summary_by_digest`. Afterwards it
  reports the 12 heaviest SELECT digests (calls, total and average time, rows examined) and runs `EXPLAIN ANALYZE` on
  each digest's sample statement. It also explains 4 targeted queries over a month or a quarter. The fixture raises
  `performance_schema_max_sql_text_length` and the digest lengths to 16 KB so the EF statements are captured whole.
- The report is written to `RG_PERF_OUT` (Markdown) and to the test output.

### Hardware and load caveats

The machine is a shared 4-vCPU Linux VM. Other builds and test suites ran at the same time, so the 1-minute load average
was 4 to 15 during the runs (each report records it). The "before" run had load ≈ 13. The final run had load ≈ 4.6.
Part of the before/after gain on unchanged endpoints is therefore load, not code. Treat small differences as noise. The
claims made here are the index plans and the `/audit/verify` rewrite, which are visible in `EXPLAIN ANALYZE`.

## Results: full scale, after this slice

Run 2026-10-16 13:45 UTC, load average 4.56 / 5.82 / 7.98.

| Endpoint | n | p50 ms | p95 ms | max ms | 2 s budget |
|---|---:|---:|---:|---:|---|
| POST /payroll/runs (generate 2026-03, 200 employees) | 3 | 341.7 | 344.1 | 344.1 | pass |
| GET /tasks (page 1, 50) | 20 | 15.5 | 26.8 | 29.6 | pass |
| GET /tasks?status=Closed (pages 1–20) | 20 | 20.6 | 26.9 | 28.2 | pass |
| GET /tasks?assigneeId (board, 100) | 20 | 14.9 | 19.5 | 20.5 | pass |
| GET /tasks (employee board, 100) | 20 | 14.2 | 21.1 | 21.9 | pass |
| GET /tasks/{id} (detail, task with most bugs) | 20 | 16.0 | 28.4 | 40.0 | pass |
| GET /deductions (manager, page 1) | 20 | 26.7 | 43.9 | 48.0 | pass |
| GET /deductions?status=Applied&applyMonth=2025-06 | 20 | 15.6 | 20.1 | 20.6 | pass |
| GET /deductions?employeeId | 20 | 14.9 | 18.8 | 20.2 | pass |
| GET /deductions?openDispute=true | 20 | 11.1 | 15.5 | 16.2 | pass |
| GET /deductions/preview (open month) | 20 | 35.4 | 48.3 | 50.7 | pass |
| GET /payroll/runs/{id} (generated preview) | 20 | 12.8 | 16.9 | 19.8 | pass |
| GET /payroll/runs/{id} (locked) | 20 | 13.7 | 19.7 | 28.0 | pass |
| GET /payroll/runs/{id}/employees/{e} | 20 | 9.9 | 18.8 | 19.0 | pass |
| GET /payroll/runs | 20 | 5.1 | 7.1 | 24.4 | pass |
| GET /payslips (finance, month) | 20 | 9.2 | 14.7 | 19.4 | pass |
| GET /payslips (employee, own) | 20 | 5.6 | 9.3 | 15.1 | pass |
| GET /payslips/{id}/pdf | 20 | 35.5 | 74.6 | 77.7 | pass |
| GET /reports/rework-summary (24 months) | 20 | 65.3 | 86.6 | 92.8 | pass |
| GET /reports/by-employee (24 months) | 20 | 76.4 | 92.8 | 117.7 | pass |
| GET /reports/by-module (24 months) | 20 | 78.0 | 108.3 | 124.6 | pass |
| GET /reports/payroll-register | 20 | 11.6 | 16.0 | 19.3 | pass |
| GET /reports/payroll-register?format=xlsx | 20 | 113.7 | 267.6 | 267.7 | pass |
| GET /reports/rework-summary?format=xlsx (24 months) | 20 | 83.0 | 110.5 | 144.0 | pass |
| GET /audit-events (page 1) | 20 | 47.6 | 60.7 | 61.6 | pass |
| GET /audit-events?entityType=Deduction (pages 1–5) | 20 | 17.4 | 23.6 | 25.1 | pass |
| GET /audit/verify (71k-entry chain) | 20 | 833.9 | 1079.9 | 1188.2 | pass |
| GET /notifications (inbox) | 20 | 4.8 | 7.0 | 9.2 | pass |
| GET /notifications/unread-count | 20 | 3.4 | 4.3 | 4.4 | pass |

The seeder also times payroll generate for each of the 23 past months, as history grows: min 263 ms, median 325 ms,
max 1,065 ms. The maximum is the first, cold request.

### Before this slice (same data, load ≈ 13)

Only one endpoint failed: **`GET /audit/verify` had p50 4,417 ms and p95 5,791 ms (FAIL)**. Every other endpoint
passed. The slowest of them were payroll-register xlsx (p95 390 ms), rework-summary (p95 253 ms) and generate (p95 482 ms).

### CI guard scale (`RG_PERF_SCALE` default 0.25: 50 employees, 1,250 tasks, 5,000 logs, 24 months)

All endpoints pass. Generate p95 128 ms, `/audit/verify` p95 352 ms. The whole test takes about 2 minutes.

## EXPLAIN: before and after

### 1. `/audit/verify`: window functions replaced by an indexed self-join

Before (`LAG() OVER (ORDER BY Sequence)` twice over the whole table, materialised into temporary tables):

```
-> Limit: 1 row(s)  (actual time=4277..4277 rows=0 loops=1)
    -> Sort: x.Sequence, limit input to 1 row(s) per chunk  (actual time=4277..4277 rows=0 loops=1)
        -> Filter: ((x.`Hash` <> x.Recomputed) or (x.PrevHash <> x.PrevActual) or ...)  (actual time=4277..4277 rows=0 loops=1)
            -> Table scan on x  (actual time=4114..4155 rows=71377 loops=1)
                -> Materialize  (actual time=4114..4114 rows=71377 loops=1)
                    -> Window aggregate with buffering: lag(AuditEvents.`Hash`) OVER (ORDER BY a.Sequence )   (actual time=2575..3815 rows=71377 loops=1)
                        -> Table scan on <temporary> ...
                            -> Window aggregate with buffering: lag(AuditEvents.Sequence) OVER (ORDER BY a.Sequence ) ...
                                -> Sort: a.Sequence ...
                                    -> Table scan on a  (actual time=0.105..100 rows=71377 loops=1)
```

After: each entry is joined to `Sequence - 1` through the unique `IX_AuditEvents_Sequence`. The query walks that index
in order and stops at the first break. A missing predecessor is a gap; for sequence 1 the genesis hash applies. The
reasons (`hash`, `gap`, `link`) and the first-break order are unchanged, and `AuditChainTests` still pass.

```
-> Limit: 1 row(s)  (cost=17010 rows=1) (actual time=898..898 rows=0 loops=1)
    -> Filter: ((a.`Hash` <> sha2(concat_ws(...),256)) or ((p.Id is null) and (a.Sequence <> 1)) or (a.PrevHash <> coalesce(p.`Hash`,'000…')))  (actual time=898..898 rows=0 loops=1)
        -> Nested loop left join  (actual time=0.06..430 rows=71377 loops=1)
            -> Index scan on a using IX_AuditEvents_Sequence  (actual time=0.0473..180 rows=71377 loops=1)
            -> Single-row index lookup on p using IX_AuditEvents_Sequence (Sequence=(a.Sequence - 1))  (actual time=0.00325..0.00327 rows=1 loops=71377)
```

The remaining time is mostly the SHA-256 recomputation over 71k rows, which is the point of the check.

### 2. `TimeLogs(ApprovalStatus, WorkDate)`, replacing `TimeLogs(ApprovalStatus)`

Before: payroll generate (salary base for hourly and daily staff) and the reports read every approved log, then
filtered by date:

```
-> Filter: ((t.WorkDate >= ...'2024-04-01') and (t.WorkDate <= ...'2026-03-31'))  (cost=588 rows=1085) (actual time=1.09..59.7 rows=19930 loops=1)
    -> Index lookup on t using IX_TimeLogs_ApprovalStatus (ApprovalStatus='Approved')  (cost=588 rows=9765) (actual time=1.07..57.1 rows=19930 loops=1)
```

After, for one pay month (the salary-base shape):

```
-> Aggregate using temporary table  (actual time=3.4..3.4 rows=642 loops=1)
    -> Index range scan on TimeLogs using IX_TimeLogs_ApprovalStatus_WorkDate over (ApprovalStatus = 'Approved' AND '2026-02-01' <= WorkDate <= '2026-02-28') ...
```

The approval queue (`ApprovalStatus = 'Pending' ORDER BY WorkDate`) also reads the index in order, with no sort. For the
24-month report range nearly every row qualifies, so MySQL correctly picks a 20k-row table scan (≈ 23 ms) instead.

### 3. `TaskStateHistory(ToStatus, ChangedAtUtc)`: by-module reopen counts

Before: `Table scan on t (rows=18921)`, filtered to 1,500 reopen rows (110 ms of the 145 ms statement).
After, over 24 months: MySQL drives from `WorkTasks` with `IX_TaskStateHistory_TaskId` lookups (43 ms). Over a quarter
it uses the new index directly:

```
-> Nested loop inner join  (actual time=0.099..0.925 rows=227 loops=1)
    -> Index range scan on t using IX_TaskStateHistory_ToStatus_ChangedAtUtc over (ToStatus = 'Reopened' AND '2025-12-31 18:30:00' <= ChangedAtUtc < '2026-03-31 18:30:00') ...
    -> Single-row index lookup on w using PRIMARY (Id=t.TaskId) ...
```

### 4. `Bugs(FoundDate)`: reports bucket bugs by found month

```
-> Covering index range scan on Bugs using IX_Bugs_FoundDate over ('2026-01-01' <= FoundDate <= '2026-03-31')  (rows=227) (actual time=0.0283..0.145 rows=227 loops=1)
```

Before this index, every bucket query scanned the whole Bugs table.

### Other statements reviewed and left alone

- `SELECT COUNT(*) FROM AuditEvents LEFT JOIN Employees …` (audit list total) reads 71k index entries in ≈ 25 ms.
  It grows with history; see Limits.
- The by-module payroll item rollup does a 4k-row scan of `PayrollRunItems` (≈ 23 ms) and the by-employee rollups read
  ≈ 4.6k `PayrollRunEmployees` rows. Both are cheap at this volume.
- Task, deduction, payslip and notification lists use existing indexes (`IX_TaskAssignments_*`,
  `IX_Deductions_EmployeeId_ApplyMonth`, `IX_Deductions_Status_ApplyMonth`, `IX_Notifications_RecipientEmployeeId_Id`).
  None of them has a statement in the top 12.

## Changes in this slice

- Migration `PerformanceIndexes`: adds `IX_TimeLogs_ApprovalStatus_WorkDate` (and drops `IX_TimeLogs_ApprovalStatus`,
  which is its prefix), `IX_TaskStateHistory_ToStatus_ChangedAtUtc` and `IX_Bugs_FoundDate`.
- `AuditChainSql.FirstBreak` rewritten as an indexed self-join (no schema change).
- Test fixture: MySQL starts with 16 KB performance_schema statement and digest text, so EF statements can be explained.

## Limits and deferred work

- `/audit/verify` is O(total audit entries). It costs about 12 µs per entry here, so ≈ 2 s at around 165k entries on
  this machine. Verifying from signed checkpoints, or verifying only the entries after the last verified sequence, would
  make it O(new entries). That is deferred because it changes what the endpoint attests.
- The audit list's total count (`COUNT(*)` over the whole chain) also grows linearly. It is ≈ 25 ms at 71k.
- Timings come from an in-process host over the test transport against a tmpfs MySQL. A deployed API adds network and
  TLS time and runs on a durable disk (fsync). Repeat the test on production-like hardware before relying on these
  absolute numbers.
- The time spent writing payroll runs, `SELECT … LAST_INSERT_ID()` round trips for 200 run-employee rows, is EF's
  per-row identity fetch. It is fine at 200 employees; batching it would matter at thousands.

## Reproduce

Needs Docker and the .NET 8 SDK. From the repository root:

```bash
cd backend
dotnet build
# CI guard: quarter volume, asserts p95 <= 2 s (about 2 minutes)
RG_PERF=1 dotnet test --no-build --filter Category=Performance
# Full volume with the Markdown report (timings, top statements, EXPLAIN ANALYZE)
RG_PERF=1 RG_PERF_SCALE=1 RG_PERF_OUT=$PWD/perf-report.md dotnet test --no-build --filter Category=Performance
```

Without `RG_PERF=1` the test is reported as skipped, so the normal `dotnet test` run is unaffected.
