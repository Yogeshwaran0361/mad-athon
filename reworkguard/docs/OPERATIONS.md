# Operations

How to configure, deploy, watch and repair a running ReworkGuard. The AWS reference deployment is in [AWS_DEPLOYMENT.md](AWS_DEPLOYMENT.md); it is documentation only and has not been provisioned or executed.

## Configuration

Settings come from `appsettings.json`, then environment variables (`Section__Key`), then (Development only) user-secrets. **Secrets are never in the repository**: locally `scripts/setup-dev.sh` generates them into `.env` (git-ignored) and user-secrets; in AWS they come from Secrets Manager into the task definition.

| Key | Secret | Notes |
| --- | --- | --- |
| `ConnectionStrings__Default` | yes | App user (least privilege, below). |
| `Jwt__SigningKey` | yes | ≥ 32 characters; the API refuses to start without it. Rotating it signs everyone out within 15 minutes (refresh still works). |
| `Database__MigrateOnStartup` | | `true` only in Development/compose. Production runs the migrator image. |
| `Database__SeedOnStartup`, `Database__SeedPassword` | password yes | Demo users; never in production. |
| `Email__Provider` | | `Log` (default, writes a log line), `Smtp` (MailHog locally) or `Ses`. `Email__FromAddress`, `Email__Ses__Region`, `Email__Ses__ConfigurationSetName`. |
| `Notifications__AppBaseUrl` | | Used in email links. |
| `Cors__AllowedOrigins__0..n` | | Only needed when the SPA is on another origin. |
| `ForwardedHeaders__KnownNetworks__0..n` | | CIDR of the proxy/ALB. Without it the client address is the proxy's and every user shares one login rate-limit bucket. |
| `RateLimiting__LoginPermitLimit` | | Sign-in/refresh attempts per minute per address (default 10). |
| `Auth__MaxFailedLogins`, `Auth__LockoutMinutes`, `Auth__RefreshTokenDays`, `Auth__SecureCookie` | | Defaults 5, 15, 7, true. Keep `SecureCookie` true anywhere served over HTTPS. |
| `Jobs__Enabled`, `Jobs__RunServer`, `Jobs__WorkerCount`, `Jobs__*Cron` | | Turn `RunServer` off on HTTP-only instances. Cron times are Asia/Kolkata. |
| `Serilog__MinimumLevel__Default` | | `Information`; JSON to stdout outside Development. |
| `DevTools__ClockControl` | | Development only; ignored in other environments. Never set in production. |

## Database

- **Migrations** are EF Core migrations. Production: build the `migrator` target of `backend/Dockerfile` (an EF migrations bundle) and run it once per release **with a privileged account** before rolling the API. Migrations define `Down`, but rollbacks were not exercised against production-sized data, so try one on a restored snapshot first. To roll back: `./efbundle --connection "<admin conn>" <PreviousMigrationName>`. Locally: `dotnet ef database update -p backend/src/ReworkGuard.Infrastructure -s backend/src/ReworkGuard.Api`.
- MySQL must run with `log_bin_trust_function_creators=1` (or the migrating user needs SUPER/SET_USER_ID) because migrations create triggers. On RDS set it in the parameter group.
- **Hangfire tables** (`Hangfire_*`) are created by Hangfire on first start (`PrepareSchemaIfNecessary`). Either start the API once with an account that has CREATE, or run it first with the migration account; afterwards the app account needs only DML on them.
- **App user grants**: after migrations and the first start, run [`scripts/db/app-user-grants.sql`](../scripts/db/app-user-grants.sql) as an administrator and `CALL rg_grant_app_user('<user>', '%')`. It gives SELECT/INSERT only on `AuditEvents`, `TaskStateHistory`, `PayrollRunEmployees`, `PayrollRunItems`, SELECT only on `AuditChainHead` and `__EFMigrationsHistory`, and full DML elsewhere. Re-run it after a migration that adds tables.
- **Backups**: RDS automated backups (7–35 days) plus a manual snapshot before each release. Restore drill: restore a snapshot to a new instance, point a staging API at it, call `GET /api/v1/audit/verify` (it must report `valid: true`) and open the latest locked payroll run.
- **Character set** utf8mb4 / `utf8mb4_0900_ai_ci`; the audit hash depends on it.

## Running more than one API instance

- **SignalR** has no backplane: with two or more instances add the Redis backplane (ElastiCache) or use sticky sessions, otherwise some users miss live notifications (they still see them on refresh).
- **Data Protection keys** are per container. Only the Hangfire dashboard cookie uses them today; persist the key ring (S3 or the database) before relying on it across instances.
- **Hangfire** coordinates through MySQL, so several job servers are safe; jobs also take a distributed lock. Run the job server on one service if you want to keep HTTP instances lean (`Jobs__RunServer=false` on the others).

## Health and monitoring

| Signal | Where |
| --- | --- |
| Liveness / readiness | `GET /health` (process), `GET /health/ready` (database). The compose and ECS health checks use these. |
| Logs | JSON lines on stdout (Serilog compact format) with `CorrelationId`, `RequestPath`, `StatusCode`, `Elapsed`. Request bodies, tokens and query strings are not logged. Ask a user for the `correlationId` from an error and search for it. |
| Jobs | Admin → **Operations** (last run, next run, result, trigger now) or the Hangfire dashboard (`/hangfire`, Admin, separate 30-minute cookie from Operations). |
| Email/in-app delivery | Admin → Operations → outbox: pending, failed and dead messages with the last error; **Retry** re-queues a dead message. |
| Audit integrity | `GET /api/v1/audit/verify` (Manager/Admin, also on the **Audit** page). Schedule it daily from monitoring and alert on `valid: false`. |

Suggested alarms: ALB 5xx rate, `/health/ready` failures, outbox messages in Dead > 0, audit verify invalid, RDS CPU/storage, failed login spikes (`LoginFailed`/`LockedOut` audit actions).

## Routine tasks

| Task | How |
| --- | --- |
| Unlock an account | `POST /api/v1/admin/employees/{id}/unlock` (Admin; no button in the UI yet). Lockouts also expire after 15 minutes. |
| Sign someone out everywhere | Reset their password or deactivate them; every refresh token is revoked and access tokens expire within 15 minutes. |
| Change a policy | Admin → Settings. Takes effect for new computations; existing runs keep the snapshot they used. |
| Add holidays | Admin → Settings → Holidays. They affect dispute windows (working days). |
| Month end | Finance generates the run for the month, reviews, a Manager approves, Finance finalizes then locks. See [USER_GUIDE.md](USER_GUIDE.md). |
| Correct a deduction after payroll | Never edit rows. Change the source (hours, review, attribution) or call `POST /api/v1/deductions/{id}/compensate` (Manager, API only); the next run picks up the difference. |
| Export the audit log | Audit page → Export CSV (up to 100,000 rows per export; the export itself is audited). |

## Incident notes

- **Audit verify reports a break**: treat as a security incident. Do not repair rows. Snapshot the database, record the reported `firstBrokenSequence` and `reason`, compare with the latest backup to find what changed, and check MySQL general/audit logs for the account used.
- **Payroll finalize refuses with `payroll.inputs_changed`**: something changed after approval (a line approved, a dispute resolved). Regenerate the run and approve again.
- **Outbox messages dead**: check the last error (SMTP/SES credentials, sandbox limits on SES), fix, then Retry.
- **Everyone gets 429 on sign-in**: `ForwardedHeaders__KnownNetworks` does not include the proxy network.
