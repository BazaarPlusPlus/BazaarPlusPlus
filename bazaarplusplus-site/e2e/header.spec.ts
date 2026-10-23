import { expect, test, type Page } from '@playwright/test';

import { expectNoUnexpectedRequests, installGoldenRoutes } from './golden';
import { settle } from './page';

// Exercise the scrollbar appearing on long pages, as it does in a desktop browser.
test.use({ launchOptions: { ignoreDefaultArgs: ['--hide-scrollbars'] } });

async function headerPositions(page: Page) {
  return page
    .locator('header')
    .first()
    .evaluate((header) =>
      ['img', 'nav', '[role="group"]'].map((selector) => {
        const rect = header.querySelector(selector)!.getBoundingClientRect();
        return { x: rect.x, y: rect.y, width: rect.width, height: rect.height };
      })
    );
}

for (const width of [390, 1143, 1600]) {
  test(`navigation stays in place across page widths and lengths at ${width}px`, async ({
    page,
  }) => {
    await page.setViewportSize({ width, height: 884 });
    const routes = await installGoldenRoutes(page);
    await page.goto('/support');
    await settle(page);
    const initial = await headerPositions(page);

    const navigate = async (name: string) => {
      await page.getByRole('navigation').getByRole('link', { name, exact: true }).click();
      await settle(page);
      expect(await headerPositions(page), `header after navigating to ${name}`).toEqual(initial);
    };
    await navigate('统计');
    await navigate('教程');
    await navigate('下载');
    await navigate('支持');
    expectNoUnexpectedRequests(routes);
  });
}
