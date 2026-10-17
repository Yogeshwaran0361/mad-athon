# Demo script: Ravi's Login module

A 10-minute walk-through of the whole Pay-Once Ledger, on the local Docker stack. The data is created through the public API by `scripts/seed-demo.sh` (PowerShell: `scripts/seed-demo.ps1`), so the demo shows real behaviour, not fixtures.

## Set up (once)

```bash
./scripts/setup-dev.sh          # writes .env with random passwords, prints the demo password
docker compose up --build -d    # MySQL, API, web, MailHog
./scripts/seed-demo.sh          # the story below; prints every step
```

Open http://localhost:8080 (app) and http://localhost:8025 (MailHog, every email sent). Staff accounts use `SEED_PASSWORD` from `.env`; Ravi and Meera use `DEMO_USER_PASSWORD` (default `<SEED_PASSWORD>-Demo2026`).

## The story

| # | Who | What happens | Show |
| --- | --- | --- | --- |
| 1 | Admin | Creates **Ravi Kumar** (monthly ₹60,000 → ₹375/h at 160 h) and **Meera Iyer** (hourly ₹300), both under Tara Lead; a *Net Banking Portal* project (45-day warranty) with a *Login* module | Admin → Employees |
| 2 | Lead | Plans "Login module: OTP sign-in" for Ravi and Meera | Tasks |
| 3 | Ravi, Meera | Log 6 h and 2 h of **regular** work; the lead approves both. Ravi delivers the task | Task history: Assigned → InProgress → Completed |
| 4 | QA | Reports "Login accepts an expired OTP" (High) **inside warranty**; the task reopens | Task page: warranty end date, Reopened |
| 5 | Ravi | Logs 4 h of **corrective** work against the bug; the lead approves | Task moves to UnderFix; corrective hours kept apart from regular |
| 6 | Manager | Reviews the bug: **OwnDefect**, caused by the original delivery | Bug review |
| 7 | System | Draft lines by approved regular hours: Ravi 75% (6 of 8 h), Meera 25%. Ravi 4 h × ₹375 × 75% = **₹1,125.00**; Meera 4 h × ₹300 × 25% = **₹300.00** | Deduction queue |
| 8 | Manager | **Notifies** both lines; emails arrive | MailHog; Ravi's bell |
| 9 | Ravi | **Disputes**: one hour was a shared config issue | My deductions → Dispute |
| 10 | Manager | **Upholds** with 3 h: Ravi's line is cancelled and replaced by 3 h × ₹375 × 75% = **₹843.75** | Deduction queue; Ravi gets the decision by email |
| 11 | (clock) | The Development-only clock moves past the 3-working-day window | — |
| 12 | Manager | **Approves** the lines | |
| 13 | Finance → Manager → Finance | Payroll for the apply month: **Generate → Review → Approve → Finalize → Lock** | Payroll run: reconciliation all green |
| 14 | Ravi | Payslip published: ₹843.75 applied, well under his cap (20% of ₹60,000 = ₹12,000) | Payslips → PDF |
| 15 | Meera | Hourly staff are capped on hours worked in the pay month. Meera has no hours in that month, so her cap is ₹0 and the ₹300.00 **carries forward** to the next run | Payroll run → Meera |
| 16 | Manager | **Audit** → Verify chain: valid; every step above is in the trail with who and when | Audit page |

Figures in rows 7, 10, 14 and 15 follow from the rules in [BUSINESS_RULES.md](BUSINESS_RULES.md); the script prints the actual line amounts as it runs.

## Talking points

- Corrective time is a different kind of time from the moment it is logged; it cannot be relabelled.
- Nobody is charged without a manager's root-cause decision, a notice, and a chance to dispute.
- The cap protects take-home pay; anything above it carries forward instead of disappearing or doubling up.
- After finalize nothing is edited: corrections become compensating lines in the next open month.
- The audit trail is hash-chained and the app's database account cannot alter it.

## Extra scenes (optional)

- **Outside warranty**: report a bug on a task delivered more than 45 days ago; it is stored but never deducts.
- **Not their fault**: review a bug as `SpecAmbiguity`; no lines are raised.
- **Late change**: after locking, the manager changes the attribution split; a compensating line appears in the next month.
- **Lockout**: five wrong passwords lock an account for 15 minutes; the response never reveals it.

## Reset

`docker compose down -v && docker compose up --build -d && ./scripts/seed-demo.sh`
