import { expect, test, type Locator, type Page } from '@playwright/test';

import {
  expectNoUnexpectedRequests,
  goldenJson,
  goldenSources,
  installGoldenRoutes,
  readJson,
  type GoldenSources
} from './golden';
import { settle } from './page';

type Manifest = { downloads: Record<string, { mainlandUrl?: string }> };
type AndroidManifest = {
  downloads: Record<string, { app: { url: string; size: number } }>;
};

function androidFixture(): AndroidManifest {
  return readJson(
    '../release/fixtures/latest/android-aarch64.json'
  ) as AndroidManifest;
}

function windowsWithoutMirror(): Manifest {
  const manifest = readJson(
    '../release/fixtures/latest/windows-x86_64.json'
  ) as Manifest;
  delete manifest.downloads['windows-x86_64'].mainlandUrl;
  return manifest;
}

function androidWithApp(
  app: Partial<AndroidManifest['downloads'][string]['app']>
) {
  const manifest = androidFixture();
  Object.assign(manifest.downloads['android-aarch64'].app, app);
  return manifest;
}

const STATES: Record<string, () => Partial<GoldenSources>> = {
  'both platforms load': () => ({}),
  'mac manifest responds 503': () => ({ mac: { status: 503 } }),
  'windows manifest has no mainlandUrl': () => ({
    windows: { json: windowsWithoutMirror() }
  }),
  'both manifests fail': () => ({
    windows: { status: 503 },
    mac: { status: 503 }
  }),
  'android manifest responds 404': () => ({ android: { status: 404 } }),
  'android apk is outside the release origin': () => ({
    android: {
      json: androidWithApp({ url: 'https://untrusted.example/a.apk' })
    }
  }),
  'android apk has a real size': () => ({
    android: { json: androidWithApp({ size: 2_834_563_072 }) }
  })
};

const PLATFORM_TITLES = ['Windows', 'macOS'] as const;

async function describeLink(page: Page, title: string, name: RegExp | string) {
  const target = platformCard(page, title).getByRole('link', { name });
  return {
    href: await target.getAttribute('href'),
    disabled: (await target.getAttribute('aria-disabled')) === 'true',
    target: await target.getAttribute('target')
  };
}

function platformCard(page: Page, title: string) {
  return page.locator('article').filter({
    has: page.getByRole('heading', { level: 2, name: title, exact: true })
  });
}

async function describeCard(page: Page, title: string) {
  const card = platformCard(page, title);
  const version = await card
    .getByText('Current version', { exact: true })
    .locator('xpath=following-sibling::*[1]')
    .textContent();
  return {
    version: version?.trim(),
    adaptedGames: await adaptedGames(card),
    onlineGame: await onlineGame(card),
    primary: await describeLink(page, title, /^Download \./),
    mirror: await describeLink(page, title, `${title} mainland mirror`)
  };
}

async function text(locator: Locator) {
  return (await locator.textContent())?.trim();
}

async function fact(card: Locator, label: string) {
  const key = card.locator('dt', { hasText: label });
  if ((await key.count()) === 0) return null;
  return text(key.locator('xpath=following-sibling::dd[1]'));
}

async function adaptedGames(card: Locator) {
  const key = card.locator('dt', { hasText: 'Adapted game' });
  if ((await key.count()) === 0) return null;
  const rows = key.locator('xpath=following-sibling::dd[1]/span/span');
  return (await rows.allTextContents()).map((row) => row.trim());
}

async function onlineGame(card: Locator) {
  const key = card.locator('dt', { hasText: 'Online game' });
  if ((await key.count()) === 0) return null;
  const value = key.locator('xpath=following-sibling::dd[1]');
  return {
    version: await text(value.locator('.tabular-nums')),
    conclusion: await text(value.locator('.bpp-badge'))
  };
}

async function describeAndroid(page: Page) {
  const card = platformCard(page, 'Android');
  if ((await card.count()) === 0) return null;
  const download = card.getByRole('link', { name: 'Download .apk' });
  return {
    badge: await text(card.getByText('Experimental', { exact: true })),
    requirements: await text(
      card.getByText('Android 11+ · arm64', { exact: true })
    ),
    notice: await text(card.getByText(/^Features differ from desktop/)),
    version: await fact(card, 'Current version'),
    adaptedGames: await adaptedGames(card),
    onlineGame: await onlineGame(card),
    size: await fact(card, 'Size'),
    sha256: (await card.locator('details code').count())
      ? await text(card.locator('details code').last())
      : null,
    download: {
      href: await download.getAttribute('href'),
      disabled: (await download.getAttribute('aria-disabled')) === 'true'
    },
    mirror: await describeLink(page, 'Android', 'Android mainland mirror'),
    feedback: await text(
      card.getByRole('button', { name: /^QQ feedback group/ })
    ),
    stepsTitle: await card.locator('ol').getAttribute('aria-label'),
    steps: await card.locator('ol > li > div').evaluateAll((items) =>
      items.map((item) => ({
        title: item.children[0].textContent,
        body: item.children[1].textContent
      }))
    ),
    limitsTitle: await text(
      card.locator('summary', { hasText: 'Good to know' })
    ),
    limits: await card.locator('ul > li').allTextContents()
  };
}

async function describeDownloads(page: Page) {
  const cards = await Promise.all(
    PLATFORM_TITLES.map((title) => describeCard(page, title))
  );
  const fallback = page
    .locator('p', { hasText: 'Cannot reach the latest version right now' })
    .getByRole('link', { name: 'GitHub Release' });
  return {
    cards: Object.fromEntries(
      PLATFORM_TITLES.map((title, index) => [title, cards[index]])
    ),
    android: await describeAndroid(page),
    fallback:
      (await fallback.count()) === 0
        ? null
        : {
            text: (
              await page
                .getByText(/Cannot reach the latest version right now/)
                .textContent()
            )?.trim(),
            href: await fallback.getAttribute('href')
          }
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
    expect(
      routes.hits.android,
      'the page requests the Android manifest'
    ).toBeGreaterThan(0);
    expectNoUnexpectedRequests(routes);
  });
}

test('each Platform Release Manifest degrades only its own card (download-cards.json)', () => {
  expect(Object.keys(result)).toEqual(Object.keys(STATES));
  expect(goldenJson(result)).toMatchSnapshot('download-cards.json');
});

test('the Android feedback button copies the QQ group number', async ({
  page,
  context
}) => {
  await context.grantPermissions(['clipboard-read', 'clipboard-write']);
  await installGoldenRoutes(page, goldenSources());
  await page.goto('/download?lang=en');
  await settle(page);
  const button = platformCard(page, 'Android').getByTitle(
    'Copy the group number'
  );
  await button.click();
  await expect(button).toHaveText('Group number copied');
  expect(await page.evaluate(() => navigator.clipboard.readText())).toBe(
    '1083470305'
  );
});

test('Android keeps full checksums and usage notes behind expandable summaries', async ({
  page,
  context
}) => {
  await context.grantPermissions(['clipboard-read', 'clipboard-write']);
  await installGoldenRoutes(page, goldenSources());
  await page.goto('/download?lang=en');
  await settle(page);
  const card = platformCard(page, 'Android');
  const checksum = card
    .locator('details')
    .filter({ has: page.locator('summary', { hasText: 'SHA-256' }) });
  const hash = checksum.locator('code').last();
  const fullHash = (await hash.textContent())!;
  await expect(hash).toBeHidden();
  await expect(checksum.locator('summary')).not.toContainText(fullHash);
  await checksum.locator('summary').click();
  await expect(hash).toBeVisible();
  await checksum.getByRole('button', { name: 'Copy full SHA-256' }).click();
  await expect(checksum.getByRole('button')).toHaveText('SHA-256 copied');
  expect(await page.evaluate(() => navigator.clipboard.readText())).toBe(
    fullHash
  );
  const notes = card
    .locator('details')
    .filter({ has: page.locator('summary', { hasText: 'Good to know' }) });
  await expect(notes.locator('ul')).toBeHidden();
  await notes.locator('summary').click();
  await expect(notes.locator('ul')).toBeVisible();
});

test.describe('Game Status', () => {
  test('each card shows the adapted game, the online game and the conclusion', async ({
    page
  }) => {
    const routes = await installGoldenRoutes(page, {
      ...goldenSources(),
      gameStatus: { json: readJson('../release/fixtures/game-status.json') },
      androidGameStatus: {
        json: readJson('../release/fixtures/android-game-status.json')
      }
    });
    await page.goto('/download?lang=en');

    const mac = platformCard(page, 'macOS');
    await expect(mac.locator('dt', { hasText: 'Online game' })).toBeVisible();
    await expect(
      platformCard(page, 'Android').locator('dt', { hasText: 'Online game' })
    ).toBeVisible();
    const games = async (title: string) => {
      const card = platformCard(page, title);
      return {
        adapted: await adaptedGames(card),
        online: await onlineGame(card)
      };
    };
    expect(await games('macOS')).toEqual({
      adapted: ['1.0.13081'],
      online: { version: '1.0.13081', conclusion: 'Adapted' }
    });
    expect(await games('Windows')).toEqual({
      adapted: ['1.0.13057'],
      online: { version: '1.0.13082', conclusion: 'Not adapted yet' }
    });
    expect(await games('Android')).toEqual({
      adapted: ['1.0.12825 · Publisher', '1.0.6 · Google Play'],
      online: { version: '1.0.12830', conclusion: 'Adapted' }
    });
    expectNoUnexpectedRequests(routes);
  });
});
