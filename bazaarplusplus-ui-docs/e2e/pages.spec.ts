import { expect, test } from '@playwright/test';
import { mkdirSync } from 'node:fs';
import { join } from 'node:path';
import { pages } from '../src/pages';
for (const [key, title, en] of pages) {
  test(key, async ({ page }, info) => {
    const errors: string[] = [];
    page.on('console', (m) => {
      if (m.type() === 'error') errors.push(m.text());
    });
    page.on('pageerror', (e) => errors.push(e.message));
    await page.goto('/' + key);
    await expect(
      page.getByRole('heading', { level: 1, name: title, exact: true })
    ).toBeVisible();
    if (key === 'enchantment-fx')
      await expect
        .poll(() =>
          page.locator('canvas').first().getAttribute('width').then(Number)
        )
        .toBeGreaterThan(0);
    if (key === 'day-card') {
      await page.getByText('当天决策', { exact: true }).click();
      await expect(page.getByText('商店', { exact: true })).toBeVisible();
    }
    if (key === 'decision-explorer') {
      await page.getByRole('button', { name: '买入 · 飞鱼' }).click();
      await expect(page.locator('.bpp-moment-title')).toHaveText('买入 · 飞鱼');
      await page.getByRole('button', { name: '上一步' }).click();
      await expect(page.locator('.bpp-moment-title')).toHaveText(
        '买入 · 皮皮虾'
      );
    }
    expect(
      await page.evaluate(
        () => document.documentElement.scrollWidth <= innerWidth
      )
    ).toBe(true);
    const out = process.env.BPP_UI_DOCS_SCREENSHOTS;
    if (
      out &&
      ['overview', 'cards', 'decision-explorer', 'enchantment-fx'].includes(key)
    ) {
      mkdirSync(out, { recursive: true });
      await page.screenshot({
        path: join(out, key + '-' + info.project.name + '.png'),
        fullPage: true
      });
    }
    await page.getByRole('button', { name: 'English' }).click();
    await expect(
      page.getByRole('heading', { level: 1, name: en, exact: true })
    ).toBeVisible();
    expect(errors).toEqual([]);
  });
}
test('reduced motion retains the rendered frame', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.goto('/enchantment-fx');
  await expect
    .poll(() =>
      page.locator('canvas').first().getAttribute('width').then(Number)
    )
    .toBeGreaterThan(0);
});
test('unavailable material retains plain card art', async ({ page }) => {
  await page.route('**/card-art/fx-*.json', (route) => route.abort());
  await page.goto('/enchantment-fx');
  const tile = page.locator('.bpp-card').first();
  await expect
    .poll(() =>
      tile.locator('img').evaluate((i) => (i as HTMLImageElement).naturalWidth)
    )
    .toBeGreaterThan(0);
  await expect(tile.locator('canvas')).toHaveAttribute('width', '0');
});
