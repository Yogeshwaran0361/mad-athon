import { expect, test, type Page } from '@playwright/test';
import { expectAccessible } from './support/axe';
import { loadState, password, users } from './support/env';
import { expectToast } from './support/session';

/** Presses Tab until the locator's element has focus (keyboard-only navigation, no clicks). */
async function tabTo(page: Page, name: string, max = 150) {
  const target = page.getByRole('button', { name, exact: true });
  for (let i = 0; i < max; i++) {
    await page.keyboard.press('Tab');
    if (await target.evaluate((el) => el === document.activeElement).catch(() => false)) {
      return;
    }
  }
  throw new Error(`Could not reach "${name}" with Tab in ${max} presses`);
}

test('keyboard only: sign in and move a task with the board Move menu', async ({ page }, testInfo) => {
  const state = loadState();

  await page.goto('/login');
  await expectAccessible(page, testInfo, 'login');
  await page.keyboard.press('Tab');
  await expect(page.getByLabel('Email')).toBeFocused();
  await page.keyboard.type(users.priya);
  await page.keyboard.press('Tab');
  await expect(page.getByLabel('Password')).toBeFocused();
  await page.keyboard.type(password());
  await page.keyboard.press('Enter');
  await expect(page).toHaveURL(/\/board$/);
  await expect(page.getByRole('heading', { name: 'Board' })).toBeVisible();
  await expectAccessible(page, testInfo, 'board');

  const assigned = page.getByRole('region', { name: /^Assigned/ });
  await expect(assigned.getByRole('link', { name: state.keyboardTaskTitle })).toBeVisible();

  await tabTo(page, `Move ${state.keyboardTaskTitle}`);
  await page.keyboard.press('Enter');
  const menu = page.getByRole('menu');
  const option = menu.getByRole('menuitem', { name: 'Move to In progress' });
  await expect(option).toBeVisible();
  await expectAccessible(page, testInfo, 'board-move-menu');
  for (let i = 0; i < 5 && !(await option.evaluate((el) => el === document.activeElement)); i++) {
    await page.keyboard.press('ArrowDown');
  }
  await expect(option).toBeFocused();
  await page.keyboard.press('Enter');

  await expectToast(page, `Moved "${state.keyboardTaskTitle}" to In progress.`);
  await expect(page.getByRole('region', { name: /^In progress/ }).getByRole('link', { name: state.keyboardTaskTitle })).toBeVisible();
});
