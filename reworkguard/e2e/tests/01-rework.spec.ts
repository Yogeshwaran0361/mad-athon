import { expect, test } from '@playwright/test';
import { expectAccessible } from './support/axe';
import { loadState } from './support/env';
import { expectToast, signIn } from './support/session';

test.describe.configure({ mode: 'serial' });

test('rework journey: bug reopens the task, corrective hours are approved, reviewed and notified as a deduction', async ({ browser }, testInfo) => {
  const state = loadState();
  const taskUrl = `/tasks/${state.reworkTaskId}`;
  let bugId = '';

  await test.step('QA reports a bug on the delivered task and it reopens', async () => {
    const qa = await signIn(browser, 'qa');
    await qa.goto(taskUrl);
    await expect(qa.getByRole('heading', { level: 1, name: state.reworkTaskTitle })).toBeVisible();
    await expectAccessible(qa, testInfo, 'task-detail-qa');

    const form = qa.getByRole('form', { name: 'Report a bug' });
    await form.getByLabel('Severity').selectOption('High');
    await form.getByLabel('What is wrong?').fill('Saved card list shows expired cards as usable');
    await form.getByRole('button', { name: 'Report bug and reopen task' }).click();

    const notice = qa.getByRole('alert').filter({ hasText: /recorded inside the warranty window/ });
    await expect(notice).toContainText('the task was reopened');
    bugId = /Bug #(\d+)/.exec((await notice.textContent()) ?? '')?.[1] ?? '';
    expect(bugId).not.toBe('');
    await expect(qa.getByText('Reopened', { exact: true }).first()).toBeVisible();
    await qa.context().close();
  });

  await test.step('employee logs corrective hours against the bug', async () => {
    const priya = await signIn(browser, 'priya');
    await priya.goto(taskUrl);
    await expect(priya.getByRole('heading', { name: 'Log corrective time' })).toBeVisible();
    await expectAccessible(priya, testInfo, 'task-detail-employee');

    const form = priya.getByRole('form', { name: 'Log time' });
    await expect(form.getByLabel('Bug being fixed')).toHaveValue(bugId);
    await form.getByLabel('Hours').fill('3');
    await form.getByLabel('Note (optional)').fill('Filter expired cards');
    await form.getByRole('button', { name: 'Log time' }).click();

    const row = priya.getByRole('row').filter({ hasText: 'Corrective' }).filter({ hasText: '3.00' });
    await expect(row).toContainText('Pending');
    await priya.context().close();
  });

  await test.step('team lead approves the corrective time log', async () => {
    const lead = await signIn(browser, 'lead');
    await lead.goto('/approvals');
    await expect(lead.getByRole('heading', { name: 'Approval queue' })).toBeVisible();
    const row = lead.getByRole('row').filter({ hasText: state.reworkTaskTitle }).filter({ hasText: 'Corrective' });
    await expect(row).toBeVisible();
    await expectAccessible(lead, testInfo, 'approval-queue');
    await row.getByRole('button', { name: 'Approve' }).click();
    await expect(row).toHaveCount(0);
    await lead.context().close();
  });

  await test.step('manager reviews the bug as the deliverer’s own defect and notifies the drafted line', async () => {
    const manager = await signIn(browser, 'manager');
    await manager.goto('/bug-review');
    await expect(manager.getByRole('heading', { name: 'Bug review' })).toBeVisible();
    await expect(manager.getByRole('button', { name: `Review bug ${bugId}`, exact: true })).toBeVisible();
    await expectAccessible(manager, testInfo, 'bug-review');

    await manager.getByRole('button', { name: `Review bug ${bugId}`, exact: true }).click();
    const dialog = manager.getByRole('dialog', { name: `Review bug #${bugId}` });
    await expect(dialog.getByLabel('Root cause')).toHaveValue('OwnDefect');
    await expect(dialog.getByLabel('Caused by the original delivery')).toBeChecked();
    await expectAccessible(manager, testInfo, 'bug-review-dialog');
    await dialog.getByRole('button', { name: 'Save review' }).click();
    await expectToast(manager, `Bug #${bugId} reviewed: deductible.`);

    await manager.goto('/deduction-queue');
    await expect(manager.getByRole('heading', { name: 'Deduction queue' })).toBeVisible();
    const drafts = manager.getByRole('table', { name: 'Draft deduction lines' });
    const row = drafts.getByRole('row').filter({ hasText: state.reworkTaskTitle }).filter({ hasText: `bug #${bugId}` });
    await expect(row).toHaveCount(1);
    await expect(row).toContainText('Priya Monthly');
    await expect(row).toContainText('Draft');
    await expectAccessible(manager, testInfo, 'deduction-queue-drafts');

    await row.getByRole('checkbox').check();
    await manager.getByRole('button', { name: 'Notify employees (1)' }).click();
    await expectToast(manager, 'Notified 1 line(s); dispute windows are open.');
    await expect(row).toHaveCount(0);
    await manager.context().close();
  });
});
