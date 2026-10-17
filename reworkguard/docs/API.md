# API

REST over JSON under `/api/v1`. The full list of endpoints and the roles allowed on each is [AUTHORIZATION_MATRIX.md](AUTHORIZATION_MATRIX.md), generated from the code by a test, so it cannot drift. Request and response schemas: Swagger UI at `/swagger` in Development (`/swagger/v1/swagger.json`).

## Conventions

- **Auth**: `POST /auth/login` returns `{ accessToken, expiresAtUtc, employee }` and sets the `rg_refresh` cookie (HttpOnly, Secure, SameSite=Strict, Path=/api/v1/auth, 7 days). Send `Authorization: Bearer <accessToken>` (15 minutes). `POST /auth/refresh` rotates the cookie and returns a new access token; replaying an old refresh token ends every session in its family. `POST /auth/logout` revokes the session. Login and refresh are rate limited (10/min per client address by default; `RateLimiting:LoginPermitLimit`).
- **Paging**: list endpoints take `page` (1-based) and `pageSize` (max 100) and return `{ items, page, pageSize, totalCount, totalPages }`.
- **Concurrency**: mutable resources carry `version`; state-changing calls on payroll runs take `{ "expectedVersion": n }`. A stale version is `409` with code `concurrency`.
- **Months** are `yyyy-MM`; dates `yyyy-MM-dd` (IST work dates); timestamps ISO 8601 UTC.
- **Correlation**: send `X-Correlation-ID` (8–64 chars of `A-Za-z0-9._-`) or one is generated; it is echoed in the response, included in error bodies as `correlationId` and in every log line.
- **Errors** are RFC 7807 problem details:

| Status | When | Body extras |
| --- | --- | --- |
| 400 | Validation failed | `errors` per field |
| 401 | Not signed in, bad credentials, locked account (same message), expired/revoked refresh | |
| 403 | Role not allowed, or not your record (`code`, e.g. `payslip.not_yours`, `deduction.not_owner`) | `code` |
| 404 | Not found | |
| 409 | Business rule (`code`, e.g. `task.invalid_transition`, `timelog.daily_limit`, `payroll.out_of_sequence`, `payroll.inputs_changed`) or stale `version` | `code` |
| 429 | Rate limit | |

## Endpoint groups

| Area | Main routes |
| --- | --- |
| Auth | `POST /auth/login`, `/auth/refresh`, `/auth/logout`; `GET /auth/me` |
| Tasks | `GET/POST /tasks`, `GET /tasks/{id}`, `POST /tasks/{id}/transitions`, `POST /tasks/{id}/bugs` |
| Bugs | `GET /bugs`, `GET /bugs/{id}`, `POST /bugs/{id}/verify`, `/close`, `/review`; `GET/PUT /bugs/{id}/attribution`; `POST /bugs/{id}/deductions/recalculate` |
| Time | `GET/POST /timelogs`, `GET /timelogs/approval-queue`, `POST /timelogs/{id}/approve`, `/reject` |
| Deductions | `GET /deductions`, `GET /deductions/{id}`, `GET /deductions/preview?employeeId=&periodMonth=`, `POST /deductions/notify`, `/approve`, `POST /deductions/{id}/dispute`, `/dispute/resolve`, `/acknowledge`, `/compensate` |
| Payroll | `GET /payroll/periods`, `GET/POST /payroll/runs`, `GET /payroll/runs/{id}`, `GET /payroll/runs/{id}/employees/{employeeId}`, `POST /payroll/runs/{id}/review`, `/approve`, `/finalize`, `/lock` |
| Payslips | `GET /payslips`, `GET /payslips/{id}`, `GET /payslips/{id}/pdf` |
| Reports | `GET /reports/rework-summary`, `/by-module`, `/by-employee`, `/payroll-register`; each takes `format=json|csv|xlsx|pdf` (see [REPORTS.md](REPORTS.md)) |
| Notifications | `GET /notifications`, `GET /notifications/unread-count`, `POST /notifications/{id}/read`, `POST /notifications/read-all`, `GET/PUT /notifications/preferences`; hub `/hubs/notifications` |
| Audit | `GET /audit-events`, `GET /audit-events/export.csv`, `GET /audit/verify` |
| Admin | employees (create, update, pay, password, unlock), policies, holidays, projects/modules/warranty, jobs, outbox |
| Health | `GET /health` (liveness), `GET /health/ready` (database) — anonymous |

## Example: notify, dispute, resolve

```http
POST /api/v1/deductions/notify            (Manager)
{ "deductionIds": [41, 42] }

POST /api/v1/deductions/41/dispute        (the employee on line 41, inside the window)
{ "reason": "One hour was spent on a shared config issue" }

POST /api/v1/deductions/41/dispute/resolve   (Manager)
{ "outcome": "Upheld", "justification": "Config hour confirmed", "newHours": 3, "newSharePercent": null }
```

## Example: run payroll for a month

```http
POST /api/v1/payroll/runs                 (Finance)  { "periodMonth": "2026-11" }
POST /api/v1/payroll/runs/7/review        (Finance)  { "expectedVersion": 1 }
POST /api/v1/payroll/runs/7/approve       (Manager, not the generator or reviewer) { "expectedVersion": 2 }
POST /api/v1/payroll/runs/7/finalize      (Finance)  { "expectedVersion": 3 }
POST /api/v1/payroll/runs/7/lock          (Finance)  { "expectedVersion": 4 }
```
