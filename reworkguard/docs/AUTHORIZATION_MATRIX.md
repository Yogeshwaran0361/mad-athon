# Authorization matrix

Generated from the controllers by `EndpointSecurityTests`; do not edit by hand. Every endpoint not marked public requires a signed-in user with one of the ticked roles. Row-level rules (own records, team scope) are enforced in the services and covered by the IDOR tests.

| Method | Route | Employee | QA | TeamLead | Manager | Finance | Admin |
|---|---|---|---|---|---|---|---|
| DELETE | `/api/v1/admin/holidays/{id:int}` |  |  |  |  |  | ✓ |
| GET | `/api/v1/admin/employees/{id:int}/pay` |  |  |  |  | ✓ | ✓ |
| GET | `/api/v1/admin/employees` |  |  |  |  | ✓ | ✓ |
| GET | `/api/v1/admin/holidays` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| GET | `/api/v1/admin/jobs` |  |  |  |  |  | ✓ |
| GET | `/api/v1/admin/outbox` |  |  |  |  |  | ✓ |
| GET | `/api/v1/admin/policies` |  |  |  | ✓ | ✓ | ✓ |
| GET | `/api/v1/admin/projects/{id:int}/modules` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| GET | `/api/v1/admin/projects` |  |  |  | ✓ | ✓ | ✓ |
| GET | `/api/v1/audit-events/export.csv` |  |  |  | ✓ |  | ✓ |
| GET | `/api/v1/audit-events` |  |  |  | ✓ |  | ✓ |
| GET | `/api/v1/audit/verify` |  |  |  | ✓ |  | ✓ |
| GET | `/api/v1/auth/me` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| GET | `/api/v1/bugs/{id:int}/attribution` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| GET | `/api/v1/bugs/{id:int}` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| GET | `/api/v1/bugs` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| GET | `/api/v1/change-requests/{id:int}/profit-share` |  |  |  | ✓ | ✓ | ✓ |
| GET | `/api/v1/change-requests/{id:int}` |  | ✓ | ✓ | ✓ | ✓ | ✓ |
| GET | `/api/v1/change-requests` |  | ✓ | ✓ | ✓ | ✓ | ✓ |
| GET | `/api/v1/deductions/preview` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| GET | `/api/v1/deductions/{id:int}` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| GET | `/api/v1/deductions` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| GET | `/api/v1/employees` |  |  | ✓ | ✓ | ✓ | ✓ |
| GET | `/api/v1/incentive-awards` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| GET | `/api/v1/notifications/preferences` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| GET | `/api/v1/notifications/unread-count` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| GET | `/api/v1/notifications` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| GET | `/api/v1/payroll/periods` |  |  |  | ✓ | ✓ | ✓ |
| GET | `/api/v1/payroll/runs/{id:int}/employees/{employeeId:int}` |  |  |  | ✓ | ✓ | ✓ |
| GET | `/api/v1/payroll/runs/{id:int}` |  |  |  | ✓ | ✓ | ✓ |
| GET | `/api/v1/payroll/runs` |  |  |  | ✓ | ✓ | ✓ |
| GET | `/api/v1/payslips/{id:long}/pdf` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| GET | `/api/v1/payslips/{id:long}` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| GET | `/api/v1/payslips` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| GET | `/api/v1/reports/by-employee` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| GET | `/api/v1/reports/by-module` |  |  | ✓ | ✓ | ✓ | ✓ |
| GET | `/api/v1/reports/payroll-register` |  |  |  | ✓ | ✓ | ✓ |
| GET | `/api/v1/reports/rework-summary` |  |  | ✓ | ✓ | ✓ | ✓ |
| GET | `/api/v1/tasks/{id:int}` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| GET | `/api/v1/tasks` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| GET | `/api/v1/timelogs/approval-queue` |  |  | ✓ | ✓ |  |  |
| GET | `/api/v1/timelogs` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| POST | `/api/v1/admin/employees/{id:int}/password` |  |  |  |  |  | ✓ |
| POST | `/api/v1/admin/employees/{id:int}/pay` |  |  |  |  |  | ✓ |
| POST | `/api/v1/admin/employees/{id:int}/unlock` |  |  |  |  |  | ✓ |
| POST | `/api/v1/admin/employees` |  |  |  |  |  | ✓ |
| POST | `/api/v1/admin/holidays` |  |  |  |  |  | ✓ |
| POST | `/api/v1/admin/jobs/dashboard-session` |  |  |  |  |  | ✓ |
| POST | `/api/v1/admin/jobs/{id}/trigger` |  |  |  |  |  | ✓ |
| POST | `/api/v1/admin/outbox/{id:long}/retry` |  |  |  |  |  | ✓ |
| POST | `/api/v1/admin/projects/{id:int}/modules` |  |  |  |  |  | ✓ |
| POST | `/api/v1/admin/projects` |  |  |  |  |  | ✓ |
| POST | `/api/v1/auth/login` | public | public | public | public | public | public |
| POST | `/api/v1/auth/logout` | public | public | public | public | public | public |
| POST | `/api/v1/auth/refresh` | public | public | public | public | public | public |
| POST | `/api/v1/bugs/{bugId:int}/deductions/recalculate` |  |  |  | ✓ |  |  |
| POST | `/api/v1/bugs/{id:int}/close` |  |  | ✓ | ✓ |  |  |
| POST | `/api/v1/bugs/{id:int}/review` |  |  |  | ✓ |  |  |
| POST | `/api/v1/bugs/{id:int}/verify` |  | ✓ | ✓ | ✓ |  |  |
| POST | `/api/v1/change-requests/{id:int}/approve` |  |  |  | ✓ |  |  |
| POST | `/api/v1/change-requests/{id:int}/close` |  |  |  |  | ✓ |  |
| POST | `/api/v1/change-requests/{id:int}/complete` |  |  | ✓ | ✓ |  |  |
| POST | `/api/v1/change-requests/{id:int}/reject` |  |  |  | ✓ |  |  |
| POST | `/api/v1/change-requests/{id:int}/start` |  |  | ✓ | ✓ |  |  |
| POST | `/api/v1/change-requests` |  | ✓ | ✓ | ✓ |  |  |
| POST | `/api/v1/deductions/approve` |  |  |  | ✓ |  |  |
| POST | `/api/v1/deductions/notify` |  |  |  | ✓ |  |  |
| POST | `/api/v1/deductions/{id:int}/acknowledge` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| POST | `/api/v1/deductions/{id:int}/cancel` |  |  |  | ✓ |  |  |
| POST | `/api/v1/deductions/{id:int}/compensate` |  |  |  | ✓ |  |  |
| POST | `/api/v1/deductions/{id:int}/dispute/resolve` |  |  |  | ✓ |  |  |
| POST | `/api/v1/deductions/{id:int}/dispute` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| POST | `/api/v1/incentive-awards/{id:int}/approve` |  |  |  | ✓ |  |  |
| POST | `/api/v1/incentive-awards/{id:int}/reject` |  |  |  | ✓ |  |  |
| POST | `/api/v1/notifications/read-all` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| POST | `/api/v1/notifications/{id:long}/read` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| POST | `/api/v1/payroll/runs/{id:int}/approve` |  |  |  | ✓ |  |  |
| POST | `/api/v1/payroll/runs/{id:int}/finalize` |  |  |  |  | ✓ |  |
| POST | `/api/v1/payroll/runs/{id:int}/lock` |  |  |  |  | ✓ |  |
| POST | `/api/v1/payroll/runs/{id:int}/review` |  |  |  |  | ✓ |  |
| POST | `/api/v1/payroll/runs` |  |  |  |  | ✓ |  |
| POST | `/api/v1/tasks/{id:int}/transitions` | ✓ | ✓ | ✓ | ✓ |  |  |
| POST | `/api/v1/tasks/{taskId:int}/bugs` |  | ✓ | ✓ | ✓ |  |  |
| POST | `/api/v1/tasks` |  |  | ✓ | ✓ |  |  |
| POST | `/api/v1/timelogs/{id:int}/approve` |  |  | ✓ | ✓ |  |  |
| POST | `/api/v1/timelogs/{id:int}/reject` |  |  | ✓ | ✓ |  |  |
| POST | `/api/v1/timelogs` | ✓ | ✓ | ✓ | ✓ |  |  |
| PUT | `/api/v1/admin/employees/{id:int}` |  |  |  |  |  | ✓ |
| PUT | `/api/v1/admin/policies` |  |  |  |  |  | ✓ |
| PUT | `/api/v1/admin/projects/{id:int}/warranty` |  |  |  |  |  | ✓ |
| PUT | `/api/v1/bugs/{id:int}/attribution` |  |  |  | ✓ |  |  |
| PUT | `/api/v1/notifications/preferences` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
