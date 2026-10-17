import { expect, test } from '@playwright/test';
import { expectAccessible } from './support/axe';
import { loadState } from './support/env';
import { expectToast, signIn } from './support/session';

test.describe.configure({ mode: 'serial' });

test('dispute journey: employee disputes a notified line, manager upholds it with reduced hours', async ({ browser }, testInfo) => {
  const state = loadState();
  let lineId = '';

  await test.step('employee sees the notified line in My deductions and disputes it', async () => {
    const priya = await signIn(browser, 'priya');
    await priya.goto('/my-deductions');
    await expect(priya.getByRole('heading', { name: 'My deductions' })).toBeVisible();
    const row = priya.getByRole('table', { name: 'My deduction lines' }).getByRole('row').filter({ hasText: state.reworkTaskTitle });
    await expect(row).toHaveCount(1);
    await expect(row).toContainText('Notified');
    await expect(row).toContainText('Open until');
    await expect(priya.getByText('Some lines are inside their dispute window.', { exact: false })).toBeVisible();
    await expectAccessible(priya, testInfo, 'my-deductions');

    lineId = ((await row.getByRole('cell').first().textContent()) ?? '').trim();
    await row.getByRole('button', { name: `Dispute line ${lineId}` }).click();
    const dialog = priya.getByRole('dialog', { name: `Dispute line #${lineId}` });
    await dialog.getByLabel('Reason').fill('Two of the three hours were spent on a gateway outage, not my defect.');
    await expectAccessible(priya, testInfo, 'dispute-dialog');
    await dialog.getByRole('button', { name: 'Raise dispute' }).click();
    await expectToast(priya, `Dispute raised on line #${lineId}.`);
    await expect(row).toContainText('Disputed');
    await expect(row).toContainText('Dispute: Two of the three hours');
    await priya.context().close();
  });

  await test.step('manager upholds the dispute with reduced hours', async () => {
    const manager = await signIn(browser, 'manager');
    await manager.goto('/deduction-queue');
    await manager.getByRole('tab', { name: 'Open disputes' }).click();
    const row = manager.getByRole('table', { name: 'Open disputes' }).getByRole('row').filter({ hasText: state.reworkTaskTitle });
    await expect(row).toHaveCount(1);
    await expectAccessible(manager, testInfo, 'deduction-queue-disputes');

    await row.getByRole('button', { name: `Resolve dispute on line ${lineId}` }).click();
    const dialog = manager.getByRole('dialog', { name: `Resolve dispute on line #${lineId}` });
    await dialog.getByRole('radio', { name: /Uphold/ }).check();
    await dialog.getByLabel(/New hours/).fill('1');
    await dialog.getByLabel('Justification').fill('Gateway outage confirmed in the incident log; one hour stands.');
    await expectAccessible(manager, testInfo, 'resolve-dialog');
    await dialog.getByRole('button', { name: 'Resolve' }).click();
    await expectToast(manager, `Dispute on line #${lineId} upheld.`);
    await expect(row).toHaveCount(0);
    await manager.context().close();
  });

  await test.step('employee sees the outcome and the reduced replacement line', async () => {
    const priya = await signIn(browser, 'priya');
    await priya.goto('/my-deductions');
    const table = priya.getByRole('table', { name: 'My deduction lines' });
    const rows = table.getByRole('row').filter({ hasText: state.reworkTaskTitle });
    await expect(rows.filter({ hasText: '→ Upheld: Gateway outage confirmed' })).toHaveCount(1);
    const replacement = rows.filter({ hasNotText: 'Dispute:' });
    await expect(replacement).toHaveCount(1);
    await expect(replacement.getByRole('cell').nth(5)).toHaveText('1');
    await expectAccessible(priya, testInfo, 'my-deductions-outcome');
    await priya.context().close();
  });
});
