import { expect, test, type Page } from '@playwright/test';

import { expectNoUnexpectedRequests, goldenJson, installGoldenRoutes } from './golden';
import { historyActions, recordHistory, relativeUrl, settle } from './page';

// The inputs of test/router.test.ts, rendered through the real App on the preview build.
const INPUTS = [
  '/',
  '/support',
  '/support/',
  '/tutorial/',
  '/download/',
  '/heroes/',
  '/heroes/Mak',
  '/archetypes',
  '/cards',
  '/builds',
  '/download/preview',
  '/release/preview',
  '/unknown',
  '/missing?lang=en',
  '/supporters/?lang=en&w=3d#lost',
  '/heroes?lang=en&w=7d&s=legend',
  '/heroes?lang=ja&w=30d&s=platinum',
  '/heroes?lang=zh&w=invalid&s=all&t=high&keep=yes',
  '/heroes?w=7d&keep=yes#trend',
  '/heroes?campaign=two%20words&w=3d',
];

/** Everything a route resolves to that a reader can see: URL, history, metadata, chrome. */
async function describeRoute(page: Page) {
  const nav = page.getByRole('navigation').first();
  const languageGroup = page.locator('header [role="group"]');
  return {
    url: relativeUrl(page),
    history: await historyActions(page),
    title: await page.title(),
    lang: await page.locator('html').getAttribute('lang'),
    h1: (await page.getByRole('heading', { level: 1 }).allTextContents()).map((text) =>
      text.trim()
    ),
    currentNav: await nav.locator('a[aria-current="page"]').allTextContents(),
    nav: await nav
      .getByRole('link')
      .evaluateAll((links) =>
        links.map((link) => ({ label: link.textContent, href: link.getAttribute('href') }))
      ),
    language: {
      current: await languageGroup.locator('[aria-current="true"]').textContent(),
      links: await languageGroup
        .getByRole('link')
        .evaluateAll((links) =>
          links.map((link) => ({ label: link.textContent, href: link.getAttribute('href') }))
        ),
    },
  };
}

const table: Record<string, unknown> = {};
const interactions: Record<string, unknown> = {};

test.describe.configure({ mode: 'serial' });

for (const input of INPUTS) {
  test(`resolves ${input}`, async ({ page }) => {
    const routes = await installGoldenRoutes(page);
    await recordHistory(page);
    await page.goto(input);
    await settle(page);
    table[input] = await describeRoute(page);
    expectNoUnexpectedRequests(routes);
  });
}

test('internal clicks push, popstate refreshes, and the language switch keeps the path', async ({
  page,
}) => {
  const routes = await installGoldenRoutes(page);
  await recordHistory(page);
  // One document, so pushes, popstate, and history accumulate.
  await page.goto('/tutorial?lang=en');
  await settle(page);
  interactions.start = await describeRoute(page);

  await page.getByRole('navigation').first().getByRole('link', { name: 'Support' }).click();
  await settle(page);
  interactions['click primary nav "Support"'] = await describeRoute(page);

  await page.goBack();
  await expect(page).toHaveURL(/\/tutorial\?lang=en$/);
  await settle(page);
  interactions['history back (popstate)'] = await describeRoute(page);

  await page.locator('header [role="group"]').getByRole('link', { name: '中' }).click();
  await settle(page);
  interactions['click language switch "中"'] = await describeRoute(page);
  expectNoUnexpectedRequests(routes);
});

test('the route table matches routes.json', () => {
  expect(Object.keys(table)).toEqual(INPUTS);
  expect(goldenJson({ inputs: table, interactions })).toMatchSnapshot('routes.json');
});
