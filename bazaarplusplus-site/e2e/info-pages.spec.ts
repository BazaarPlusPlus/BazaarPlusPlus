import { expect, test, type Page } from '@playwright/test';

import { expectNoUnexpectedRequests, installGoldenRoutes } from './golden';
import { settle } from './page';

async function introPositions(page: Page) {
  return page.locator('main').evaluate((main) => {
    const heading = main.querySelector('h1')!;
    const intro = heading.nextElementSibling;
    return {
      titleTop: heading.getBoundingClientRect().top,
      introTop: intro?.getBoundingClientRect().top ?? null,
      contentTop: main.children[1].getBoundingClientRect().top,
    };
  });
}

for (const width of [320, 390, 640, 1143]) {
  for (const locale of ['zh', 'en'] as const) {
    test(`info page intros stay aligned at ${width}px in ${locale}`, async ({ page }) => {
      await page.setViewportSize({ width, height: 884 });
      const routes = await installGoldenRoutes(page);
      await page.goto(`/tutorial?lang=${locale}`);
      await settle(page);
      const initial = await introPositions(page);

      const navigate = async (name: string) => {
        await page.getByRole('navigation').getByRole('link', { name, exact: true }).click();
        await settle(page);
        expect(await introPositions(page), `intro after navigating to ${name}`).toEqual(initial);
      };

      await navigate(locale === 'zh' ? '下载' : 'Download');
      await navigate(locale === 'zh' ? '支持' : 'Support');
      expectNoUnexpectedRequests(routes);
    });
  }
}
