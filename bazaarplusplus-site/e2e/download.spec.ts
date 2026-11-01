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
type AndroidManifest = { downloads: Record<string, { app: { url: string; size: number } }> };

function androidFixture(): AndroidManifest {
  return readJson('../release/fixtures/latest/android-aarch64.json') as AndroidManifest;
}

function windowsWithoutMirror(): Manifest {
  const manifest = readJson('../release/fixtures/latest/windows-x86_64.json') as Manifest;
  delete manifest.downloads['windows-x86_64'].mainlandUrl;
  return manifest;
}

function androidWithApp(app: Partial<AndroidManifest['downloads'][string]['app']>) {
  const manifest = androidFixture();
  Object.assign(manifest.downloads['android-aarch64'].app, app);
  return manifest;
}

const STATES: Record<string, () => Partial<GoldenSources>> = {
  'both platforms load': () => ({}),
  'mac manifest responds 503': () => ({ mac: { status: 503 } }),
  'windows manifest has no mainlandUrl': () => ({ windows: { json: windowsWithoutMirror() } }),
  'both manifests fail': () => ({ windows: { status: 503 }, mac: { status: 503 } }),
  'android manifest responds 404': () => ({ android: { status: 404 } }),
  'android apk is outside the release origin': () => ({
    android: { json: androidWithApp({ url: 'https://untrusted.example/a.apk' }) },
  }),
  'android apk has a real size': () => ({
    android: { json: androidWithApp({ size: 2_834_563_072 }) },
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

/** The value beside a key in the Android card's facts list, or null when the row is absent. */
async function fact(card: Locator, label: string) {
  const key = card.locator('dt', { hasText: label });
  if ((await key.count()) === 0) return null;
  return text(key.locator('xpath=following-sibling::dd[1]'));
}

/** The Android entry as a visitor reads it; an unavailable card has no APK facts. */
async function describeAndroid(page: Page) {
  const card = platformCard(page, 'Android');
  if ((await card.count()) === 0) return null;
  const download = card.getByRole('link', { name: 'Download .apk' });
  return {
    badge: await text(card.getByText('Experimental', { exact: true })),
    notice: await text(card.getByText(/^The Android build is experimental/)),
    version: await fact(card, 'Current version'),
    gameVersion: await fact(card, 'Game version'),
    size: await fact(card, 'Size'),
    sha256: await fact(card, 'SHA-256'),
    download: {
      href: await download.getAttribute('href'),
      disabled: (await download.getAttribute('aria-disabled')) === 'true',
    },
    feedback: await text(card.getByRole('button', { name: /^QQ feedback group/ })),
    stepsTitle: await text(card.getByRole('heading', { level: 3 }).first()),
    steps: await card.locator('ol > li').locator('span:not([aria-hidden])').allTextContents(),
    limitsTitle: await text(card.getByRole('heading', { level: 3 }).nth(1)),
    limits: await card.locator('ul > li').allTextContents(),
  };
}

/** What a visitor can act on in each card, plus whether the GitHub fallback appears. */
async function describeDownloads(page: Page) {
  const cards = await Promise.all(PLATFORM_TITLES.map((title) => describeCard(page, title)));
  const fallback = page
    .locator('p', { hasText: 'Cannot reach the latest version right now' })
    .getByRole('link', { name: 'GitHub Release' });
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

test('a missing or invalid Android manifest leaves the Android card unavailable', () => {
  type Downloads = { cards: unknown; fallback: unknown; android: Record<string, unknown> | null };
  const loaded = result['both platforms load'] as Downloads;
  const missing = result['android manifest responds 404'] as Downloads;
  expect(result['android apk is outside the release origin']).toEqual(missing);
  expect({ cards: missing.cards, fallback: missing.fallback }).toEqual({
    cards: loaded.cards,
    fallback: loaded.fallback,
  });
  expect(missing.android).toMatchObject({
    badge: loaded.android?.badge,
    notice: loaded.android?.notice,
    version: 'Unavailable',
    gameVersion: null,
    size: null,
    sha256: null,
    download: { href: '#', disabled: true },
    feedback: loaded.android?.feedback,
    stepsTitle: loaded.android?.stepsTitle,
    steps: loaded.android?.steps,
    limitsTitle: loaded.android?.limitsTitle,
    limits: loaded.android?.limits,
  });
});

test('each Platform Release Manifest degrades only its own card (download-cards.json)', () => {
  expect(Object.keys(result)).toEqual(Object.keys(STATES));
  expect(goldenJson(result)).toMatchSnapshot('download-cards.json');
});

test('the Android feedback button copies the QQ group number', async ({ page, context }) => {
  await context.grantPermissions(['clipboard-read', 'clipboard-write']);
  await installGoldenRoutes(page, goldenSources());
  await page.goto('/download?lang=en');
  await settle(page);
  const button = platformCard(page, 'Android').getByTitle('Copy the group number');
  await button.click();
  await expect(button).toHaveText('Group number copied');
  expect(await page.evaluate(() => navigator.clipboard.readText())).toBe('1083470305');
});
