import { expect, test } from '@playwright/test';

import {
  expectNoUnexpectedRequests,
  goldenSources,
  installGoldenRoutes
} from './golden';
import {
  SAMPLE_RUN_ID,
  SAMPLE_TEMPLATE_IDS,
  mockAccountApi,
  mockCardArt,
  runDetailHandlers
} from './account-api';
import { settle } from './page';

const ROUTES: Record<string, string> = {
  'run-detail': `/records/${SAMPLE_RUN_ID}?lang=en`,
  support: '/support',
  tutorial: '/tutorial',
  download: '/download',
  heroes: '/heroes',
  'not-found': '/unknown'
};

for (const width of [1280, 390]) {
  for (const [route, href] of Object.entries(ROUTES)) {
    test(`${route} at ${width}px`, async ({ page }) => {
      test.skip(
        process.platform !== 'linux' && route !== 'run-detail',
        'Linux screenshot baselines'
      );
      await page.setViewportSize({ width, height: 900 });
      const routes = await installGoldenRoutes(page, goldenSources());
      if (route === 'run-detail') {
        await mockAccountApi(page, runDetailHandlers());
        await mockCardArt(
          page,
          SAMPLE_TEMPLATE_IDS,
          'a176e286-6309-4652-ab61-afcb7fbfa7de'
        );
      }
      await page.goto(href);
      if (route === 'run-detail') {
        const board = page.locator('.bpp-board').first();
        await expect(board.getByText('Shiny')).toBeVisible();
        await expect(board.getByText('Microphone')).toBeVisible();
        await expect(
          board.getByRole('img', { name: 'ISO-Belle' })
        ).toBeVisible();
      }
      await settle(page);

      if (process.platform === 'linux')
        await expect(page).toHaveScreenshot(`${route}-${width}.png`, {
          fullPage: true
        });
      if (route === 'run-detail') {
        const tile = page
          .locator('.bpp-board')
          .first()
          .getByRole('button', { name: /Tambourine/ });
        await tile.focus();
        await tile.press('Enter');
        const dialog = page.getByRole('dialog', { name: 'Tambourine' });
        await expect(dialog.getByText(/Instrument/)).toBeVisible();
        await expect(dialog.getByText('This has +1 Multicast.')).toBeVisible();
        await expect(dialog.locator('[data-current=true]')).toContainText(
          'Shiny'
        );
        await page.keyboard.press('Escape');
        await expect(tile).toBeFocused();
      }
      expectNoUnexpectedRequests(routes);
    });
  }
}
