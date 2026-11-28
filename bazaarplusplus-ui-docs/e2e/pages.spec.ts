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
    if (key === 'cards')
      await expect
        .poll(() =>
          page.locator('canvas').first().getAttribute('width').then(Number)
        )
        .toBeGreaterThan(0);
    if (key === 'run-replay') {
      await page.getByRole('button', { name: '购买 · 飞鱼' }).click();
      await expect(page.locator('.bpp-moment-title')).toHaveText('购买 · 飞鱼');
      await page.getByRole('button', { name: '上一步' }).click();
      await expect(page.locator('.bpp-moment-title')).toHaveText(
        '购买 · 皮皮虾'
      );
      await page
        .getByRole('button', { name: '购买 · 皮皮虾' })
        .press('ArrowDown');
      await expect(page.locator('.bpp-moment-title')).toHaveText('购买 · 飞鱼');
      await expect(
        page.getByRole('button', { name: '购买 · 飞鱼' })
      ).toBeFocused();
    }
    expect(
      await page.evaluate(
        () => document.documentElement.scrollWidth <= innerWidth
      )
    ).toBe(true);
    const out = process.env.BPP_UI_DOCS_SCREENSHOTS;
    if (out && ['overview', 'cards', 'run-replay'].includes(key)) {
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
test('hovering a card shows its detail beside it', async ({ page }) => {
  await page.goto('/cards');
  await page.locator('.gallery .bpp-card').first().hover();
  await expect(page.locator('.bpp-card-preview h2')).toBeVisible();
  await page.mouse.move(0, 0);
  await expect(page.locator('.bpp-card-preview')).toHaveCount(0);
});
test('one switch shows every enchantment in each detail', async ({ page }) => {
  await page.goto('/card-detail');
  const effects = page.locator('.bpp-card-detail .bpp-detail-enchant');
  await expect(effects).toHaveCount(0);
  await page.getByLabel('显示全部附魔').check();
  expect(await effects.count()).toBeGreaterThan(1);
  await page.reload();
  expect(await effects.count()).toBeGreaterThan(1);
});
test('reduced motion retains the rendered frame', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.goto('/cards');
  await expect
    .poll(() =>
      page.locator('canvas').first().getAttribute('width').then(Number)
    )
    .toBeGreaterThan(0);
});
test('unavailable material retains plain card art', async ({ page }) => {
  await page.route('**/card-art/fx-*.json', (route) => route.abort());
  await page.goto('/cards');
  const tile = page.locator('.gallery').nth(1).locator('.bpp-card').first();
  await expect
    .poll(() =>
      tile.locator('img').evaluate((i) => (i as HTMLImageElement).naturalWidth)
    )
    .toBeGreaterThan(0);
  await expect(tile.locator('canvas')).toHaveAttribute('width', '0');
});
test('all enchantments render WebGL effects and Tiny reduces width by one slot', async ({
  page
}) => {
  await page.goto('/cards');
  const tiles = page.locator('.gallery').nth(1).locator('.bpp-card');
  await expect(tiles).toHaveCount(17);
  // IntersectionObserver prepares each canvas once it enters the viewport.
  for (let i = 0; i < 17; i++) {
    const tile = tiles.nth(i);
    // eslint-disable-next-line no-await-in-loop
    await tile.scrollIntoViewIfNeeded();
    // eslint-disable-next-line no-await-in-loop
    await expect
      .poll(() => tile.locator('canvas').getAttribute('width').then(Number))
      .toBeGreaterThan(0);
  }
  const tinyMedium = page.getByRole('button', { name: '降落伞 · 钻石 · 迷你' });
  const tinyLarge = page.getByRole('button', {
    name: '霜绝祭坛 · 传奇 · 迷你'
  });
  await expect(tinyMedium).toHaveAttribute('data-size', '1');
  await expect(tinyLarge).toHaveAttribute('data-size', '2');
  await tinyMedium.scrollIntoViewIfNeeded();
  await tinyMedium.hover();
  await expect(
    page.locator('.bpp-card-preview .bpp-detail-art')
  ).toHaveAttribute('data-size', '1');
  await expect(
    page.locator('.bpp-card-preview .bpp-detail-tags')
  ).toContainText('小型');
});
