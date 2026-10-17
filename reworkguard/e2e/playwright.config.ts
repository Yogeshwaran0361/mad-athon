import { defineConfig, devices } from '@playwright/test';

// Playwright 1.56.1 uses Chromium revision 1194; E2E_CHROMIUM points at another binary if that revision is not installed.
const baseURL = process.env.E2E_BASE_URL ?? 'http://127.0.0.1:4173';

export default defineConfig({
  testDir: './tests',
  // The journeys build on each other's data (rework -> dispute -> payroll), so they run in file order on one worker.
  fullyParallel: false,
  workers: 1,
  retries: 0,
  forbidOnly: !!process.env.CI,
  timeout: 90_000,
  expect: { timeout: 15_000 },
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : [['list']],
  globalSetup: './tests/global-setup.ts',
  use: {
    baseURL,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    acceptDownloads: true,
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'], launchOptions: { executablePath: process.env.E2E_CHROMIUM || undefined } },
    },
  ],
});
