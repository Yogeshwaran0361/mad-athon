# Reports

All reports live under `GET /api/v1/reports/...`, are computed with grouped SQL (one query per figure family) and take
`format=json|xlsx|csv|pdf` (default `json`). File downloads are named `<report>_<from>_<to>.<ext>`
(`payroll-register_<month>.<ext>` for the register).

## Common rules

- **Months** are `yyyy-MM`. A range (`from`, `to`) is inclusive, `from <= to`, at most 36 months; anything else is 400.
- **Hours** are approved time logs only, bucketed by `WorkDate` (an IST date).
- **Bugs** are bucketed by `FoundDate`.
- **Finalized run** of a month = the run recorded in `PayrollPeriods.FinalizedRunId` (status Finalized or Locked).
  Money figures come only from those runs; previews, superseded and unfinalized runs never count.
- **Disputes** are bucketed by when they were raised, converted to IST.
- **Money signs** are as stored in the run: `refunds` is negative (money returned), `netAdjustment = -refunds - applied`.

## Role scoping

| Report | Employee / QA | TeamLead | Manager / Finance / Admin |
| --- | --- | --- | --- |
| rework-summary | 403 | team only | all |
| by-employee | own row only | self + direct reports | all |
| by-module | 403 | team only | all |
| payroll-register | 403 | 403 | all |

"Team only" means: hours and payroll figures of the lead and their direct reports; bugs and reopens on tasks with at
least one of them assigned.

## rework-summary

`?from=&to=` -> one row per month in the range (months with no activity included as zeros).

| Field | Definition |
| --- | --- |
| regularHours / correctiveHours | Sum of approved Regular / Corrective log hours with `WorkDate` in the month |
| reworkPercent | `corrective / (regular + corrective) * 100`, rounded half away from zero to 2 dp; 0 when there are no hours |
| bugsReported | Bugs with `FoundDate` in the month |
| bugsDeductible | Of those, bugs deductible under the current policy: within warranty, confirmed caused by the original delivery, root cause OwnDefect (or QaMiss when `QaMissDeductible` is on). A later dispute does not change this |
| deductionsApplied / refunds | Sum of `Applied` / `Refunds` over the employees of the month's finalized run (`PeriodMonth` = the month) |

## by-employee

`?from=&to=` -> one row per employee with at least one non-zero figure, ordered by name.

| Field | Definition |
| --- | --- |
| correctiveHours | Approved corrective hours in the range |
| deductionsApplied | Sum of `Applied` across the finalized runs of months in the range |
| carriedForward | `CarriedForward` in the latest finalized run in the range (0 when none) |
| disputesRaised / disputesUpheld | Disputes the employee raised in the range / of those, outcome Upheld |

## by-module

`?from=&to=` -> one row per project module with any activity, ordered by project then module.

| Field | Definition |
| --- | --- |
| bugs | Bugs on the module's tasks with `FoundDate` in the range |
| reopens | Task status changes to Reopened (one per bug report on a delivered task) in the range |
| correctiveHours | Approved corrective hours on the module's tasks in the range |
| deductionAmount | Sum of `PayrollRunItems.Applied` in finalized runs in the range, for lines of bugs on the module's tasks (refund items are negative, so this is net) |

## payroll-register

`?periodMonth=` -> the month's finalized run, one row per employee ordered by employee code, with name and pay mode as
snapshotted in the run: `employeeCode, employeeName, payMode, salaryBase, capAmount, carriedIn, currentDue, refunds,
applied, carriedForward, settledManually, netAdjustment`. 409 `report.month_not_finalized` when the month has no
finalized run. Manager, Finance and Admin only.

## Exports

- **xlsx** (ClosedXML): one sheet, bold frozen header row, numbers stored as numbers (`#,##0.00` for amounts and hours).
  Text is always written as a string cell, never a formula.
- **csv**: RFC 4180 (CRLF, quoting of `,` `"` CR LF, doubled quotes), UTF-8 with BOM. Text cells starting with
  `=`, `+`, `-`, `@`, tab or CR are prefixed with `'` so spreadsheets do not evaluate them. Numbers use `.` and 2 dp.
- **pdf** (QuestPDF, Community license set in code): landscape A4 table with title, range and page numbers.
