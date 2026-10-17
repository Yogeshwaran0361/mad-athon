# End-to-end tests

Playwright browser tests with axe accessibility scans, in `e2e/`.

## Run

Requirements: Docker, the .NET 8 SDK, Node 22 and Playwright's Chromium (revision 1194, matching `@playwright/test` 1.56.1).

```bash
cd e2e
npm ci
npx playwright install chromium   # skip if the browser is already installed (PLAYWRIGHT_BROWSERS_PATH)
npm run e2e                       # CI: CI=1 npm run e2e (adds an HTML report in playwright-report/)
```

`npm run e2e` (`run-e2e.mjs`) does the following:

1. Starts a throwaway `mysql:8.0` container on a random local port.
2. Builds the frontend (`npm run build`) and the API (`dotnet build`).
3. Runs the API with `dotnet run` in `Development`. The connection string, JWT signing key and demo seed password come from environment variables that are generated fresh for each run and never written to disk. `Database:MigrateOnStartup` and `Database:SeedOnStartup` apply the EF Core migrations and the demo seed (`DbSeeder`).
4. Serves the built frontend with `vite preview`, whose `/api` and `/hubs` proxy points at the API.
5. Runs `playwright test`, then stops the processes and removes the container, even if a test fails.

Extra arguments go to Playwright, e.g. `npm run e2e -- --headed` or `npm run e2e -- -g payroll`.

Environment knobs:

| Variable | Effect |
| --- | --- |
| `E2E_SKIP_BUILD=1` | Reuse the existing `frontend/dist` and API build |
| `E2E_VERBOSE=1` | Stream API and vite output |
| `E2E_KEEP_DB=1` | Leave the MySQL container running after the run, for debugging |
| `E2E_API_PORT`, `E2E_WEB_PORT` | Fixed ports instead of random free ones |
| `E2E_MYSQL_IMAGE` | Another MySQL 8 image |
| `E2E_CHROMIUM` | Path to a Chromium binary, if revision 1194 is not installed |

To run against a stack that is already up: `E2E_BASE_URL=… E2E_API_URL=… E2E_PASSWORD=<seed password> npx playwright test`.

## What's covered

`tests/global-setup.ts` creates the test data through the public API, on top of the demo seed. The team lead creates a task for Priya; Priya starts it and logs 6 h of regular time; the lead approves the log; Priya delivers the task. The lead also creates a second, newly assigned task for the keyboard check.

The journeys run in file order on a single worker, because each one builds on the data the previous one left:

| Spec | Journey (all through the UI) |
| --- | --- |
| `01-rework` | QA reports a bug on the delivered task, and the task reopens. Priya logs 3 corrective hours against the bug. The team lead approves the log in the approval queue. The manager reviews the bug as the deliverer's own defect (deductible) and notifies the drafted deduction line. |
| `02-dispute` | Priya finds the notified line in **My deductions** and disputes it. The manager upholds the dispute with the hours cut to 1. Priya sees the outcome and the 1-hour replacement line. |
| `03-payroll` | Finance generates the run for the current month and marks it reviewed. The manager approves it. Finance finalizes and locks it. Priya opens her payslip and downloads the PDF; the test checks that the download starts with `%PDF-`. |
| `04-keyboard` | Sign-in with the keyboard only (Tab, typing, Enter). On the board, the test reaches the card's **Move…** button with Tab, opens the menu with Enter, picks *Move to In progress* with the arrow keys and Enter, and checks the card in its new column. |

The dispute window is a policy setting (`DisputeWindowDays`, at least 1 working day), so no journey waits for it to close. The dispute is settled through the resolution path instead, and the payroll journey does not depend on approved lines. No test-only backend hook exists.

### Accessibility

Every page a journey visits is scanned with `@axe-core/playwright` against the WCAG 2.1 A/AA tags (`wcag2a`, `wcag2aa`, `wcag21a`, `wcag21aa`), and so is every dialog it opens. Serious or critical violations fail the test. Minor and moderate findings are attached to the test result as JSON. No axe rules are disabled.

Issues the scans found, now fixed in the frontend:

- **Navigation lists** (`list`, serious): the drawer's `<ul>` held a `<div>` subheader and bare `<a>` items. Each subheader is now an `<li>`, and each link is wrapped in a `ListItem` (`<li>`).
- **Colour contrast** (`color-contrast`, serious): with MUI's default `info` (#0288d1) and `warning` (#ed6c02) colours, white text on filled chips and coloured text on outlined buttons fell below 4.5:1. The theme now uses darker `info` (#0277bd) and `warning` (#b45309) colours and `contrastThreshold: 4.5`.
