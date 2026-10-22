import { expect, test, type Locator, type Page } from '@playwright/test';

import {
  expectNoUnexpectedRequests,
  goldenJson,
  goldenSources,
  installGoldenRoutes,
  readJson,
  type GoldenSources,
} from './golden';
import { settle } from './page';

type Manifest = { downloads: Record<string, { mainlandUrl?: string }> };
type AndroidManifest = { downloads: Record<string, { apk: { url: string; size: number } }> };

function androidFixture(): AndroidManifest {
  return readJson('../release/fixtures/latest/android-aarch64.json') as AndroidManifest;
}

function windowsWithoutMirror(): Manifest {
  const manifest = readJson('../release/fixtures/latest/windows-x86_64.json') as Manifest;
  delete manifest.downloads['windows-x86_64'].mainlandUrl;
  return manifest;
}

function androidWithApk(apk: Partial<AndroidManifest['downloads'][string]['apk']>) {
  const manifest = androidFixture();
  Object.assign(manifest.downloads['android-aarch64'].apk, apk);
  return manifest;
}

const STATES: Record<string, () => Partial<GoldenSources>> = {
  'both platforms load': () => ({}),
  'mac manifest responds 503': () => ({ mac: { status: 503 } }),
  'windows manifest has no mainlandUrl': () => ({ windows: { json: windowsWithoutMirror() } }),
  'both manifests fail': () => ({ windows: { status: 503 }, mac: { status: 503 } }),
  'android manifest responds 404': () => ({ android: { status: 404 } }),
  'android apk is outside the release origin': () => ({
    android: { json: androidWithApk({ url: 'https://untrusted.example/a.apk' }) },
  }),
  'android apk has a real size': () => ({
    android: { json: androidWithApk({ size: 2_834_563_072 }) },
  }),
};

const PLATFORM_TITLES = ['Windows', 'macOS'] as const;

async function describeLink(page: Page, title: string, name: RegExp | string) {
  const target = platformCard(page, title).getByRole('link', { name });
  return {
    href: await target.getAttribute('href'),
    disabled: (await target.getAttribute('aria-disabled')) === 'true',
    target: await target.getAttribute('target'),
  };
}

function platformCard(page: Page, title: string) {
  return page
    .locator('article')
    .filter({ has: page.getByRole('heading', { level: 2, name: title, exact: true }) });
}

async function describeCard(page: Page, title: string) {
  const version = await platformCard(page, title)
    .getByText('Current version', { exact: true })
    .locator('xpath=following-sibling::*[1]')
    .textContent();
  return {
    version: version?.trim(),
    primary: await describeLink(page, title, /^Download \./),
    mirror: await describeLink(page, title, `${title} mainland mirror`),
  };
}

async function text(locator: Locator) {
  return (await locator.textContent())?.trim();
}

/** The Android entry as a visitor reads it, or null when the page shows none. */
async function describeAndroid(page: Page) {
  const card = platformCard(page, 'Android');
  if ((await card.count()) === 0) return null;
  const download = card.getByRole('link', { name: 'Download .apk' });
  return {
    version: await text(
      card.getByText('Current version', { exact: true }).locator('xpath=following-sibling::*[1]')
    ),
    apk: await text(card.getByText(/^The Bazaar /)),
    size: await text(card.getByText(/^The Bazaar /).locator('xpath=following-sibling::*[1]')),
    sha256: await text(card.locator('code')),
    download: {
      href: await download.getAttribute('href'),
      disabled: (await download.getAttribute('aria-disabled')) === 'true',
    },
    stepsTitle: await text(card.getByRole('heading', { level: 3 })),
    steps: await card.getByRole('listitem').locator('span:not([aria-hidden])').allTextContents(),
  };
}

/** What a visitor can act on in each card, plus whether the GitHub fallback appears. */
async function describeDownloads(page: Page) {
  const cards = await Promise.all(PLATFORM_TITLES.map((title) => describeCard(page, title)));
  const fallback = page.getByRole('link', { name: 'GitHub Release' });
  return {
    cards: Object.fromEntries(PLATFORM_TITLES.map((title, index) => [title, cards[index]])),
    android: await describeAndroid(page),
    fallback:
      (await fallback.count()) === 0
        ? null
        : {
            text: (
              await page.getByText(/Cannot reach the latest version right now/).textContent()
            )?.trim(),
            href: await fallback.getAttribute('href'),
          },
  };
}

const result: Record<string, unknown> = {};

test.describe.configure({ mode: 'serial' });

for (const [state, overrides] of Object.entries(STATES)) {
  test(`download cards when ${state}`, async ({ page }) => {
    const routes = await installGoldenRoutes(page, goldenSources(overrides()));
    await page.goto('/download?lang=en');
    await settle(page);
    result[state] = await describeDownloads(page);
    expect(routes.hits.android, 'the page requests the Android manifest').toBeGreaterThan(0);
    expectNoUnexpectedRequests(routes);
  });
}

test('a missing or invalid Android manifest leaves only the desktop downloads', () => {
  const desktopOnly = { ...(result['both platforms load'] as object), android: null };
  expect(result['android manifest responds 404']).toEqual(desktopOnly);
  expect(result['android apk is outside the release origin']).toEqual(desktopOnly);
});

test('each Platform Release Manifest degrades only its own card (download-cards.json)', () => {
  expect(Object.keys(result)).toEqual(Object.keys(STATES));
  expect(goldenJson(result)).toMatchSnapshot('download-cards.json');
});
