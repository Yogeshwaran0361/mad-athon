import { expect, type Browser, type Page } from '@playwright/test';
import { password, users, type UserKey } from './env';

/** Opens a fresh browser context and signs in through the login form. */
export async function signIn(browser: Browser, who: UserKey): Promise<Page> {
  const context = await browser.newContext();
  const page = await context.newPage();
  await page.goto('/login');
  await page.getByLabel('Email').fill(users[who]);
  await page.getByLabel('Password').fill(password());
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page).not.toHaveURL(/\/login/);
  return page;
}

/** Toasts are mirrored into a permanently mounted polite live region (role=status). */
export async function expectToast(page: Page, text: string | RegExp): Promise<void> {
  await expect(page.locator('[role="status"][aria-live="polite"]').filter({ hasText: text })).toHaveCount(1);
}
