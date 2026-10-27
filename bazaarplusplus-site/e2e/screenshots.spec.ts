import { expect, test } from '@playwright/test';

import {
  expectNoUnexpectedRequests,
  goldenSources,
  installGoldenRoutes,
  type GoldenSources,
} from './golden';
import { settle } from './page';

// The UI font stack resolves to different system fonts per platform, so only Linux (the CI
// runner) has committed baselines.
test.skip(process.platform !== 'linux', 'screenshot baselines exist only for linux');

const ROUTES: Record<string, string> = {
  support: '/support',
  tutorial: '/tutorial',
  download: '/download',
  // Without an Android manifest the Android card stays on the page in its pending state.
  'download-android-pending': '/download',
  heroes: '/heroes',
  'not-found': '/unknown',
};

const SOURCES: Record<string, Partial<GoldenSources>> = {
  'download-android-pending': { android: { status: 404 } },
};

for (const width of [1280, 390]) {
  for (const [route, href] of Object.entries(ROUTES)) {
    test(`${route} at ${width}px`, async ({ page }) => {
      await page.setViewportSize({ width, height: 900 });
      const routes = await installGoldenRoutes(page, goldenSources(SOURCES[route]));
      await page.goto(href);
      await settle(page);

      await expect(page).toHaveScreenshot(`${route}-${width}.png`, { fullPage: true });
      expectNoUnexpectedRequests(routes);
    });
  }
}
