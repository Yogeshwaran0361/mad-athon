import AxeBuilder from '@axe-core/playwright';
import { expect, type Page, type TestInfo } from '@playwright/test';

const WCAG_21_A_AA = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'];

/**
 * Scans the page against WCAG 2.1 A/AA and fails on serious or critical violations. Minor and moderate findings are
 * attached to the test report so they stay visible without blocking.
 */
export async function expectAccessible(page: Page, testInfo: TestInfo, label: string): Promise<void> {
  // Let MUI transitions settle so the scan sees final colours and focus targets.
  await page.waitForLoadState('networkidle');
  await page.waitForTimeout(300);
  const results = await new AxeBuilder({ page }).withTags(WCAG_21_A_AA).analyze();
  const blocking = results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical');
  const other = results.violations.filter((v) => !blocking.includes(v));
  if (other.length > 0) {
    await testInfo.attach(`axe-${label}-non-blocking.json`, { body: JSON.stringify(other, null, 2), contentType: 'application/json' });
  }
  const summary = blocking.map((v) => `${v.impact} ${v.id}: ${v.help}\n  ${v.nodes.map((n) => `${n.target.join(' ')} ${n.html.slice(0, 120)} ${n.failureSummary ?? ''}`).join('\n  ')}`);
  expect(summary, `axe WCAG 2.1 A/AA serious/critical violations on ${label}`).toEqual([]);
}
