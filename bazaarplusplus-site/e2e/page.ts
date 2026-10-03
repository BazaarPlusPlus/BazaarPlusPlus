import { expect, type Page } from '@playwright/test';

declare global {
  interface Window {
    bppHistoryActions?: Array<{ mode: 'push' | 'replace'; href: string }>;
  }
}

/** Records every pushState/replaceState the page makes, before any page script runs. */
export async function recordHistory(page: Page): Promise<void> {
  await page.addInitScript(() => {
    const actions: Array<{ mode: 'push' | 'replace'; href: string }> = [];
    window.bppHistoryActions = actions;
    const push = history.pushState.bind(history);
    const replace = history.replaceState.bind(history);
    history.pushState = (data, unused, url) => {
      actions.push({ mode: 'push', href: String(url) });
      push(data, unused, url);
    };
    history.replaceState = (data, unused, url) => {
      actions.push({ mode: 'replace', href: String(url) });
      replace(data, unused, url);
    };
  });
}

export async function historyActions(page: Page) {
  return page.evaluate(() => window.bppHistoryActions ?? []);
}

/** Waits until the route has rendered its data: no busy page and no pending requests. */
export async function settle(page: Page): Promise<void> {
  await page.waitForLoadState('networkidle');
  await expect(page.locator('main[aria-busy="true"]')).toHaveCount(0);
  await expect(page.locator('[class*="animate-pulse"]')).toHaveCount(0);
}

export function relativeUrl(page: Page): string {
  const url = new URL(page.url());
  return `${url.pathname}${url.search}${url.hash}`;
}
