import { readdirSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';

import { expect, test } from '@playwright/test';

import {
  METRICS_URL,
  expectNoUnexpectedRequests,
  goldenJson,
  installGoldenRoutes,
  readOrWriteGolden,
  siteRoot,
} from './golden';
import { settle } from './page';

// Public icon byte budgets. Raise one only with the reason in the pull request.
const ICON_BUDGETS: Record<string, number> = {
  'bazaarplusplus-icon.webp': 10_000,
  'favicon.webp': 5_000,
};

const ROUTES: Record<string, string> = {
  support: '/support',
  tutorial: '/tutorial',
  download: '/download',
  heroes: '/heroes',
  'not-found': '/unknown',
};

type RouteReport = {
  scripts: Record<string, number>;
  external: string[];
  icons: Record<string, number>;
};

/** `assets/index-AbC12xyz.js` → `index.js`, so the golden survives content hashes. */
function chunkName(pathname: string): string {
  return pathname.replace(/^.*\//, '').replace(/-[\w-]{8}\.js$/, '.js');
}

const byKey = ([a]: [string, number], [b]: [string, number]) => a.localeCompare(b);

const report: Record<string, RouteReport> = {};

test.describe.configure({ mode: 'serial' });

for (const [route, href] of Object.entries(ROUTES)) {
  test(`${route} transfers`, async ({ page }) => {
    const routes = await installGoldenRoutes(page);
    const entry: RouteReport = { scripts: {}, external: [], icons: {} };
    const pending: Array<Promise<void>> = [];
    page.on('requestfinished', (request) => {
      const url = new URL(request.url());
      if (url.host !== 'localhost:3000') {
        entry.external.push(request.url());
        return;
      }
      const name = url.pathname.replace(/^\//, '');
      if (request.resourceType() === 'script') {
        pending.push(
          (async () => {
            entry.scripts[chunkName(url.pathname)] = (await request.sizes()).responseBodySize;
          })()
        );
      } else if (name in ICON_BUDGETS) {
        pending.push(
          (async () => {
            entry.icons[name] = (await request.sizes()).responseBodySize;
          })()
        );
      }
    });

    await page.goto(href);
    await settle(page);
    // Headless Chromium never requests <link rel="icon">, so fetch the declared favicon href.
    const favicon = await page.locator('link[rel="icon"]').getAttribute('href');
    entry.icons['favicon.webp'] = (await (await page.request.get(favicon!)).body()).byteLength;
    await Promise.all(pending);

    entry.external.sort((a, b) => a.localeCompare(b));
    entry.scripts = Object.fromEntries(Object.entries(entry.scripts).sort(byKey));
    entry.icons = Object.fromEntries(Object.entries(entry.icons).sort(byKey));
    report[route] = entry;

    for (const [icon, budget] of Object.entries(ICON_BUDGETS)) {
      expect(entry.icons[icon], `${icon} transfer bytes on ${href}`).toBeLessThanOrEqual(budget);
    }
    if (route !== 'heroes') {
      expect(Object.keys(entry.scripts), `${href} must not load the /heroes chunk`).not.toContain(
        'route-pages.js'
      );
      expect(entry.external, `${href} must not request metrics`).not.toContain(METRICS_URL);
    }
    expectNoUnexpectedRequests(routes);
  });
}

test('chunk names and external sources match the committed report', () => {
  expect(Object.keys(report)).toEqual(Object.keys(ROUTES));
  const actual = goldenJson(report);
  const committed = JSON.parse(
    readOrWriteGolden(test.info(), 'route-chunks.json', actual)
  ) as Record<string, RouteReport>;
  // Names and sources compare exactly; byte counts only ride along so size changes show in diffs.
  const shape = (value: Record<string, RouteReport>) =>
    Object.fromEntries(
      Object.entries(value).map(([route, entry]) => [
        route,
        {
          scripts: Object.keys(entry.scripts),
          external: entry.external,
          icons: Object.keys(entry.icons),
        },
      ])
    );
  expect(shape(report)).toEqual(shape(committed));
});

test('the main bundle leaves Hero metrics ingestion to the lazy /heroes chunk', () => {
  const assets = resolve(siteRoot, 'dist/assets');
  const count = (prefix: string) => {
    const files = readdirSync(assets).filter(
      (file) => file.startsWith(`${prefix}-`) && file.endsWith('.js')
    );
    expect(files, `one ${prefix} chunk in dist/assets`).toHaveLength(1);
    return (
      readFileSync(resolve(assets, files[0]), 'utf8').split('analyzer-v5/heroes/latest.json')
        .length - 1
    );
  };

  expect(count('index')).toBe(0);
  expect(count('route-pages')).toBe(1);
});
