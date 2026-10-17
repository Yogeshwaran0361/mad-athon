# Deploying ReworkGuard on AWS

> **Status: documented, not executed.** Nothing in this guide has been provisioned or run against an AWS account.
> The container images, the compose stack and `scripts/seed-demo.sh` were verified locally; the ECS task
> definitions in `deploy/ecs/` were rendered locally (`RENDER_ONLY=true ./deploy/ecs/deploy.sh`) but never
> registered. Treat every command below as a reviewed plan, not a record.

## Target architecture

```
                 Route 53  ──►  ACM certificate
                                   │
Internet ──► ALB (HTTPS :443, HTTP→HTTPS redirect)
               ├─ /api/*, /hubs/*, /health*, /hangfire*  ──► target group "api"  (ECS service reworkguard-<env>-api, port 8080)
               └─ default                                ──► target group "web"  (ECS service reworkguard-<env>-web, port 8080)

Private subnets (2+ AZs):
  ECS Fargate tasks: api (ASP.NET Core 8 + Hangfire server + SignalR), web (nginx-unprivileged), migrator (one-off)
  RDS for MySQL 8.0 (Multi-AZ in production)
  VPC endpoints or NAT for ECR, Secrets Manager, CloudWatch Logs, SES
```

The ALB sends API paths straight to the API service, so the web container's own `/api` proxy is only a fallback
(set `API_UPSTREAM` to the API's ECS Service Connect name, e.g. `api.reworkguard.internal:8080`, so it still works).

| Image (ECR repository) | Built from | Notes |
| --- | --- | --- |
| `reworkguard-api` | `backend/Dockerfile` target `runtime` | Non-root (UID 1654), port 8080, read-only root FS with `/tmp` and `/home/app/.aspnet` as task volumes |
| `reworkguard-migrator` | `backend/Dockerfile` target `migrator` | Self-contained EF Core migrations bundle; reads `ConnectionStrings__Default` |
| `reworkguard-web` | `frontend/Dockerfile` | nginx-unprivileged (UID 101), port 8080, SPA fallback, security headers |

## 1. Networking and ALB

1. VPC with two public subnets (ALB) and two private subnets (tasks, RDS) across two AZs.
2. Security groups: `alb` (443 from the internet), `api` and `web` (8080 from `alb`; `api` also from `web`),
   `rds` (3306 from `api` and the migrator task's group only).
3. ALB listener rules as in the diagram. Health checks: API target group `GET /health/ready` (200), web target group
   `GET /healthz` (200). Set the idle timeout to at least 120 s; SignalR keeps WebSockets open and pings every 15 s.
4. **SignalR with more than one API task.** Each connection lives on one task and notifications are pushed by the
   task that handled the change, so with `desiredCount > 1` either:
   - enable **target group stickiness** (application cookie or `lb_cookie`) on the API target group so the
     negotiate request and the WebSocket land on the same task, *and* accept that a notification raised on task A
     reaches only clients connected to A; or
   - (recommended before scaling out) add a **Redis backplane** (`Microsoft.AspNetCore.SignalR.StackExchangeRedis`
     with Amazon ElastiCache for Redis/Valkey) so every task broadcasts to every client. This is not implemented yet
     (see "Not covered" below). Until then, run the API service with `desiredCount = 1` or rely on the in-app
     notification list, which reads from the database and is always complete.
5. Hangfire runs inside every API task and coordinates through MySQL storage (distributed locks), so recurring jobs
   do not double-run when there are several tasks.

## 2. RDS for MySQL 8.0

- Engine MySQL 8.0 (latest minor), `db.t4g.small` for staging, `db.m7g.large` Multi-AZ for production, gp3 storage
  with autoscaling, encryption at rest (KMS), `Require SSL` via `require_secure_transport=ON`.
- **Custom DB parameter group** (the default group is read-only):

  | Parameter | Value | Why |
  | --- | --- | --- |
  | `log_bin_trust_function_creators` | `1` | The migrations create **13 triggers** (`AppendOnlyGuards`, `DeductionGuards`, `PayrollGuards`: append-only audit/time-log tables, immutable finalized deductions and payroll). RDS enables binary logging for backups/replicas, and without SUPER privilege MySQL refuses `CREATE TRIGGER` unless this is on. Local compose sets the same flag. |
  | `character_set_server` | `utf8mb4` | Names and notes are Unicode. |
  | `collation_server` | `utf8mb4_0900_ai_ci` | MySQL 8 default for utf8mb4. |
  | `require_secure_transport` | `ON` | TLS only. |
  | `time_zone` | `UTC` (default) | The app stores UTC and converts to Asia/Kolkata itself. |

- Two database users (created once by the master user; store each in Secrets Manager):
  - `reworkguard_migrator`: DDL plus `TRIGGER` on the schema; used only by the migrator task.
  - `reworkguard_app`: `SELECT, INSERT, UPDATE, DELETE, EXECUTE, CREATE TEMPORARY TABLES, LOCK TABLES` on the schema
    (Hangfire's MySQL storage creates its tables on first start; either let the migrator user start the API once or
    grant `CREATE, ALTER, INDEX` to the app user for the Hangfire tables).
- Connection strings: `Server=<endpoint>;Port=3306;Database=reworkguard;User=...;Password=...;SslMode=Required`.
- **Backups**: automated backups with 14-day retention (35 for production), a daily backup window outside 09:00 IST
  (reminders run then), deletion protection on, final snapshot on delete, and AWS Backup copying weekly snapshots to
  a second region for production. Test a restore into staging each quarter; the payroll tables are append-only by
  trigger, so a restore is the only way to "undo" a finalized month.
- Enable Performance Insights and export the `error` and `slowquery` logs to CloudWatch.

## 3. Secrets Manager

| Secret | Keys | Read by |
| --- | --- | --- |
| `reworkguard/<env>/app` | `ConnectionString` (app user), `JwtSigningKey` (64 random chars) | API task (execution role) |
| `reworkguard/<env>/migrations` | `ConnectionString` (migrator user) | Migrator task (execution role) |

The task definitions in `deploy/ecs/` inject them with `secrets[].valueFrom` (`arn:...:secret:<name>:<key>::`), so
values never appear in the task definition, the image or the workflow logs. Rotate the DB passwords with Secrets
Manager rotation (single-user strategy) and restart the API service afterwards; rotating `JwtSigningKey` signs
everyone out.

Never set `Database__SeedOnStartup`, `Database__SeedPassword` or `DevTools__ClockControl=true` outside local
development. The clock control is also disabled by code outside the `Development` environment.

## 4. Amazon SES

1. **Verify the sending domain** in SES (same region as the tasks): add the three DKIM CNAME records, set a custom
   MAIL FROM domain (MX + SPF TXT), and publish a DMARC record (`p=quarantine` once reports look clean).
2. **Leave the sandbox**: request production access in the SES console (use case: transactional notifications to
   employees; expected volume; bounce/complaint handling). While in the sandbox, every recipient must be verified.
3. Create configuration set `reworkguard-<env>` with event destinations for bounces and complaints (SNS or
   CloudWatch); the task definition passes it as `Email__Ses__ConfigurationSetName`.
4. API task role permission: `ses:SendEmail` on the domain identity and the configuration set. The SDK uses the task
   role; no SMTP credentials or access keys are configured.
5. App settings: `Email__Provider=Ses`, `Email__FromAddress=no-reply@<domain>`, `Email__Ses__Region=<region>`.
   Delivery goes through the notification outbox (retries with back-off), so SES throttling only delays mail.

## 5. IAM

- **GitHub OIDC**: add the identity provider `token.actions.githubusercontent.com` (audience `sts.amazonaws.com`) and
  one deploy role per GitHub environment, trust-limited to
  `repo:<org>/<repo>:environment:build`, `...:environment:staging` and `...:environment:production`.
  Build role: ECR push to the three repositories. Deploy roles: `ecs:RegisterTaskDefinition`, `ecs:RunTask`,
  `ecs:DescribeTasks`, `ecs:UpdateService`, `ecs:DescribeServices` on the cluster, and `iam:PassRole` for the two
  task roles below. No long-lived access keys anywhere.
- `reworkguard-<env>-ecs-execution`: `AmazonECSTaskExecutionRolePolicy` + `secretsmanager:GetSecretValue` on the two
  secrets (+ `kms:Decrypt` if they use a customer key).
- `reworkguard-<env>-api-task`: `ses:SendEmail` (above). Nothing else.

## 6. ECS Fargate

1. Cluster `reworkguard-<env>` with Container Insights; ECR repositories with scan-on-push and a lifecycle policy
   (keep the last 30 images).
2. CloudWatch log groups `/reworkguard/<env>/{api,web,migrator}` with 30-day retention (90 for production), KMS
   encrypted. The API logs to stdout in the default console format; add a metric filter + alarm on `fail:` lines.
3. Services `reworkguard-<env>-api` (desired 1 until the SignalR backplane exists, see 1.4) and
   `reworkguard-<env>-web` (desired 2), rolling deployments with the circuit breaker and rollback enabled,
   `minimumHealthyPercent=100`, `maximumPercent=200`, ECS Service Connect namespace `reworkguard.internal`.
4. Task definitions live in `deploy/ecs/*.task-definition.json` and are rendered by `deploy/ecs/deploy.sh`
   (placeholders such as `${API_IMAGE}`, `${AWS_ACCOUNT_ID}`). Containers run read-only with explicit UIDs; the
   API's container health check calls `/health` with bash's `/dev/tcp` (no curl in the image).
5. **Data-protection keys**: ASP.NET Core keeps them in `/home/app/.aspnet` (a task volume), so they change when a task
   restarts. Only the Hangfire dashboard cookie depends on them (admins re-open the dashboard from the app). Persist
   them (e.g. `PersistKeysToAWSSystemsManager`) before relying on anything else protected by them.
6. Forwarded headers: the API does not yet process `X-Forwarded-For`/`-Proto`, so the login rate limiter
   (10/min per client address) sees the ALB's address. See "Not covered".

## 7. Release flow and migrations

`.github/workflows/cd.yml`:

1. Push to `main` → build the `api`, `migrator` and `web` images, push to ECR (`sha-<12>` tag, SBOM + provenance),
   Trivy-gate them, then deploy to **staging**.
2. Tag `vX.Y.Z` → same build, then deploy to **production** after a manual approval (configure required reviewers on
   the `production` environment in GitHub).
3. Each deploy (`deploy/ecs/deploy.sh`):
   1. renders and registers the three task definitions;
   2. **runs the migrator as a one-off Fargate task** and waits for it; a non-zero exit stops the deploy before any
      service changes (logs in `/reworkguard/<env>/migrator`);
   3. updates the API and web services and waits until they are stable;
   4. smoke-tests `GET /health/ready` through the ALB.

The API image never migrates in AWS (`Database__MigrateOnStartup=false`). Migrations must stay backward compatible
with the previous API version for the length of a rolling deploy (expand first, contract in a later release).
Take a manual RDS snapshot before a release that contains a destructive migration.

## 8. Cost notes (ap-south-1, on-demand, rough monthly figures)

| Item | Staging | Production |
| --- | --- | --- |
| Fargate: api 1 vCPU/2 GB ×1, web 0.25 vCPU/0.5 GB ×2 | ~$45 | ~$45 (×2 api later: ~$80) |
| ALB | ~$20 | ~$25 |
| RDS MySQL `db.t4g.small` single-AZ, 20 GB / `db.m7g.large` Multi-AZ, 100 GB | ~$30 | ~$330 |
| NAT gateway (or ~$7/endpoint/AZ for ECR, logs, secrets, SES VPC endpoints) | ~$35 | ~$70 |
| Secrets Manager (2 secrets), CloudWatch Logs (few GB), ECR storage | ~$5 | ~$15 |
| SES | $0.10 per 1,000 emails | same |

Levers: Fargate Spot for staging, Savings Plans / RDS reserved instances for production, scheduled scale-to-zero of
staging outside office hours, and VPC endpoints instead of NAT when egress is small. Check the AWS Pricing
Calculator before committing; these numbers are estimates, not quotes.

## Not covered (follow-ups)

- Infrastructure as code (Terraform/CDK) for everything above; today it is a manual/console checklist.
- SignalR Redis backplane for multiple API tasks.
- `UseForwardedHeaders` with the ALB as a known network, so rate limiting and logs see the client address.
- Persisted data-protection keys.
- WAF on the ALB (AWS managed common rule set + rate-based rule on `/api/v1/auth/login`).
