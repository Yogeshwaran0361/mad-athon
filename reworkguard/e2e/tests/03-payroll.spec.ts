import { readFileSync } from 'node:fs';
import { expect, test, type Page } from '@playwright/test';
import { expectAccessible } from './support/axe';
import { loadState } from './support/env';
import { signIn } from './support/session';

test.describe.configure({ mode: 'serial' });

async function confirmStep(page: Page, label: string) {
  await page.getByRole('button', { name: label, exact: true }).click();
  const dialog = page.getByRole('dialog', { name: new RegExp(`^${label.replace('&', '\\&')} run`) });
  await expect(dialog).toBeVisible();
  await dialog.getByRole('button', { name: label, exact: true }).click();
  await expect(dialog).toBeHidden();
}

test('payroll journey: generate, review, approve, finalize, lock, then the employee downloads a payslip PDF', async ({ browser }, testInfo) => {
  const { month } = loadState();
  let runUrl = '';

  await test.step('Finance generates and reviews the run', async () => {
    const finance = await signIn(browser, 'finance');
    await finance.goto('/payroll');
    await expect(finance.getByRole('heading', { name: 'Payroll', exact: true })).toBeVisible();
    await expectAccessible(finance, testInfo, 'payroll');
    await finance.getByRole('button', { name: `Generate run for ${month}` }).click();
    await expect(finance).toHaveURL(/\/payroll\/runs\/\d+$/);
    runUrl = new URL(finance.url()).pathname;
    await expect(finance.getByRole('heading', { name: new RegExp(`Payroll run ${month} v\\d+`) })).toBeVisible();
    await expectAccessible(finance, testInfo, 'payroll-run-preview');
    await finance.getByRole('button', { name: 'Mark reviewed' }).click();
    await expect(finance.getByText('Waiting for a Manager to approve.')).toBeVisible();
    await finance.context().close();
  });

  await test.step('a Manager approves', async () => {
    const manager = await signIn(browser, 'manager');
    await manager.goto(runUrl);
    await expect(manager.getByRole('button', { name: 'Approve', exact: true })).toBeEnabled();
    await expectAccessible(manager, testInfo, 'payroll-run-reviewed');
    await manager.getByRole('button', { name: 'Approve', exact: true }).click();
    await expect(manager.getByText('Waiting for Finance to finalize.')).toBeVisible();
    await manager.context().close();
  });

  await test.step('Finance finalizes and locks', async () => {
    const finance = await signIn(browser, 'finance');
    await finance.goto(runUrl);
    await confirmStep(finance, 'Finalize');
    await expect(finance.getByRole('button', { name: 'Lock & apply' })).toBeVisible();
    await confirmStep(finance, 'Lock & apply');
    await expect(finance.getByText('This run is locked and applied.')).toBeVisible();
    await expectAccessible(finance, testInfo, 'payroll-run-locked');
    await finance.context().close();
  });

  await test.step('employee opens the payslip and downloads the PDF', async () => {
    const priya = await signIn(browser, 'priya');
    await priya.goto('/payslips');
    await expect(priya.getByRole('heading', { name: 'Payslips' })).toBeVisible();
    const view = priya.getByRole('button', { name: `View payslip ${month} for Priya Monthly` });
    await expect(view).toBeVisible();
    await expectAccessible(priya, testInfo, 'payslips');

    await view.click();
    const dialog = priya.getByRole('dialog', { name: `Payslip ${month} – Priya Monthly` });
    await expect(dialog).toContainText('Salary base');
    await expect(dialog.getByRole('table', { name: 'Payslip lines' })).toBeVisible();
    await expectAccessible(priya, testInfo, 'payslip-dialog');

    const downloadPromise = priya.waitForEvent('download');
    await dialog.getByRole('button', { name: 'Download PDF' }).click();
    const download = await downloadPromise;
    expect(download.suggestedFilename()).toMatch(/\.pdf$/);
    const file = await download.path();
    expect(readFileSync(file).subarray(0, 5).toString('latin1')).toBe('%PDF-');
    await priya.context().close();
  });
});
