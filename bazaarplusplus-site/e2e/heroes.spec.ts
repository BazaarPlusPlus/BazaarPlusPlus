import { expect, test, type Page } from '@playwright/test';

import {
  expectNoUnexpectedRequests,
  goldenSources,
  heroSnapshot,
  installGoldenRoutes,
  type GoldenRoutes,
  type HeroFixture,
  type SourceReply,
} from './golden';
import { historyActions, recordHistory, relativeUrl, settle } from './page';

const WINDOWS = ['1d', '3d', '7d'] as const;
const SEGMENTS = ['all', 'legend', 'non_legend'] as const;

let routes: GoldenRoutes;

async function openHeroes(page: Page, href: string, fixture: HeroFixture = 'latest') {
  routes = await installGoldenRoutes(
    page,
    goldenSources({ metrics: { json: heroSnapshot(fixture) } })
  );
  await page.goto(href);
  await settle(page);
}

function rankingTable(page: Page) {
  return page.getByTestId('hero-ranking-table');
}

/** One tab-separated line per ranking row; hero cells contribute the hero's full name. */
async function rankingLines(page: Page): Promise<string[]> {
  return rankingTable(page).evaluate((table) => {
    const header = Array.from(table.querySelectorAll('thead th'), (cell) => {
      const sort = cell.getAttribute('aria-sort');
      return `${(cell.querySelector('button span')?.textContent ?? '').trim()}${
        sort === 'ascending' ? ' ▲' : sort === 'descending' ? ' ▼' : ''
      }`;
    });
    const rows = Array.from(table.querySelectorAll('tbody tr'), (row) =>
      Array.from(
        row.querySelectorAll('td'),
        (cell) =>
          cell.querySelector('[data-hero-badge]')?.getAttribute('data-hero-badge') ??
          (cell.textContent ?? '').trim()
      ).join('\t')
    );
    return [header.join('\t'), ...rows];
  });
}

async function rankingHeroes(page: Page): Promise<string[]> {
  return rankingTable(page)
    .locator('tbody [data-hero-badge]')
    .evaluateAll((badges) => badges.map((badge) => badge.getAttribute('data-hero-badge') ?? ''));
}

/**
 * Clicks each column header once, in order, and records the resulting hero order. A header
 * other than the active one sorts in its first-click direction, so every column's direction and
 * null handling lands in the golden.
 */
async function sortOrderLines(page: Page, headers?: string[]): Promise<string[]> {
  const pending =
    headers ??
    (await rankingTable(page).locator('thead th button > span:first-child').allTextContents());
  if (pending.length === 0) {
    return [];
  }
  const [header, ...rest] = pending;
  const table = rankingTable(page);
  await table.getByRole('button', { name: header, exact: true }).click();
  const sort = await table
    .getByRole('columnheader', { name: header, exact: true })
    .getAttribute('aria-sort');
  const line = `${header} ${sort === 'ascending' ? '▲' : '▼'}\t${(await rankingHeroes(page)).join(' ')}`;
  return [line, ...(await sortOrderLines(page, rest))];
}

async function matchupLines(page: Page): Promise<string[]> {
  const selected = page.getByTestId('selected-matchup-hero');
  const focus =
    (await selected.count()) === 0 ? '(none)' : await selected.getAttribute('aria-label');
  const list = page.getByTestId('matchup-list');
  const rows =
    (await list.count()) === 0
      ? [(await page.getByText('No matchup data yet').count()) > 0 ? '(no matchup data yet)' : '?']
      : await list.locator('li').evaluateAll((items) =>
          items.map((item) =>
            [
              item.querySelector('[data-hero-badge]')?.getAttribute('data-hero-badge'),
              ...Array.from(item.children)
                .slice(1)
                .map((child) => (child.textContent ?? '').trim()),
            ].join('\t')
          )
        );
  return [`focus\t${focus}`, ...rows];
}

test.afterEach(() => {
  expectNoUnexpectedRequests(routes);
});

test.describe('Analysis Scope ranking goldens over the analyzer snapshot', () => {
  for (const window of WINDOWS) {
    for (const segment of SEGMENTS) {
      test(`${window} ${segment}`, async ({ page }) => {
        await openHeroes(page, `/heroes?lang=en&w=${window}&s=${segment}`);
        const filters = page.getByTestId('ranking-panel').getByRole('group', { name: 'Filters' });
        await expect(filters.getByRole('button', { name: window.toUpperCase() })).toHaveAttribute(
          'aria-pressed',
          'true'
        );

        const body = [
          `# /heroes?lang=en&w=${window}&s=${segment}`,
          '## ranking',
          ...((await rankingTable(page).count()) === 0
            ? [`(no table) ${await page.getByTestId('ranking-panel').locator('h3').textContent()}`]
            : await rankingLines(page)),
          '## matchups',
          ...(await matchupLines(page)),
          '## first click on each column',
          ...((await rankingTable(page).count()) === 0 ? [] : await sortOrderLines(page)),
          '',
        ].join('\n');
        expect(body).toMatchSnapshot(`heroes-${window}-${segment}.txt`);
      });
    }
  }
});

test.describe('route states', () => {
  for (const [locale, href, errorTitle, errorBody, retry] of [
    [
      'en',
      '/heroes?lang=en',
      'Stats are temporarily unavailable',
      'The hero stats could not be loaded. Try again in a moment.',
      'Try again',
    ],
    ['zh', '/heroes', '数据暂时不可用', '暂时无法读取英雄统计数据，请稍后重试。', '重试'],
  ] as const) {
    test(`a failing snapshot shows the error state in the page chrome and retries (${locale})`, async ({
      page,
    }) => {
      let metrics: SourceReply = { status: 503 };
      routes = await installGoldenRoutes(page, goldenSources({ metrics: () => metrics }));
      await page.goto(href);

      await expect(page.getByRole('heading', { level: 2, name: errorTitle })).toBeVisible({
        timeout: 15_000,
      });
      await expect(page.getByText(errorBody)).toBeVisible();
      await expect(page.getByText(/503/)).toHaveCount(0);
      await expect(page.getByRole('navigation').first()).toBeVisible();
      await expect(page.getByRole('link', { name: 'Xinyu YANG' })).toBeVisible();
      // React Query retries a 5xx twice before it shows the error state.
      expect(routes.hits.metrics).toBe(3);

      metrics = { json: heroSnapshot('latest') };
      await page.getByRole('button', { name: retry }).click();
      await expect(rankingTable(page)).toBeVisible();
      expect(routes.hits.metrics).toBe(4);
    });
  }

  for (const [locale, href, copy] of [
    [
      'zh',
      '/heroes?lang=zh&w=invalid&s=all&t=high&keep=yes',
      {
        h1: '英雄统计',
        title: '正在准备英雄统计',
        body: '正在读取最新数据快照，请稍候。',
        progress: '正在加载英雄统计',
        canonical: '/heroes?keep=yes',
      },
    ],
    [
      'en',
      '/heroes?lang=en&w=3d',
      {
        h1: 'Hero stats',
        title: 'Preparing hero stats',
        body: 'Reading the latest data snapshot. This should only take a moment.',
        progress: 'Loading hero stats',
        canonical: '/heroes?lang=en&w=3d',
      },
    ],
  ] as const) {
    test(`a pending snapshot shows the stable loading state and canonical scope (${locale})`, async ({
      page,
    }) => {
      routes = await installGoldenRoutes(page, goldenSources({ metrics: 'hang' }));
      await recordHistory(page);
      await page.goto(href);

      await expect(page.getByRole('heading', { level: 1, name: copy.h1 })).toBeVisible();
      await expect(page.getByRole('heading', { level: 2, name: copy.title })).toBeVisible();
      await expect(page.getByText(copy.body)).toBeVisible();
      const progress = page.getByRole('progressbar', { name: copy.progress });
      await expect(progress).toBeVisible();
      await expect(progress).not.toHaveAttribute('aria-valuenow');
      await expect(page.getByText(/heroes\/latest\.json/)).toHaveCount(0);
      await expect(page.getByText(/\d+\s*\/\s*\d+/)).toHaveCount(0);
      await expect(page.getByText(/加载失败|failed/i)).toHaveCount(0);
      // Hero scope canonicalizes at the location boundary, before any data arrives.
      expect(relativeUrl(page)).toBe(copy.canonical);
      await expect.poll(() => routes.hits.metrics).toBe(1);
      await expect(page.getByRole('heading', { level: 2, name: copy.title })).toBeVisible();
      const canonicalizes = copy.canonical !== href;
      expect(await historyActions(page)).toEqual(
        canonicalizes ? [{ mode: 'replace', href: copy.canonical }] : []
      );
    });
  }
});

test.describe('Hero Analysis dashboard', () => {
  test('URL scope drives the window and segment controls and keeps the BazaarDB link', async ({
    page,
  }) => {
    await openHeroes(page, '/heroes?lang=en&w=3d&s=legend', 'threeDay');

    const bazaarDb = page.getByRole('link', { name: 'View detailed stats on BazaarDB' });
    await expect(bazaarDb).toHaveAttribute(
      'href',
      'https://bazaardb.gg/run/meta?utm_source=bazaarplusplus'
    );
    await expect(bazaarDb).toHaveAttribute('target', '_blank');
    await expect(bazaarDb.locator('img')).toHaveAttribute('src', '/bazaardb-icon.ico');

    const filters = page.getByTestId('ranking-panel').getByRole('group', { name: 'Filters' });
    await expect(filters.getByRole('button', { name: '3D' })).toHaveAttribute(
      'aria-pressed',
      'true'
    );
    await expect(filters.getByRole('button', { name: 'Legend', exact: true })).toHaveAttribute(
      'aria-pressed',
      'true'
    );
    await expect(filters.getByRole('button', { name: 'All' })).toHaveAttribute(
      'aria-pressed',
      'false'
    );
    await expect(filters.getByRole('button', { name: 'Non-Legend' })).toBeVisible();
  });

  test('one segment control drives ranking, trend, and matchups through a replace', async ({
    page,
  }) => {
    await recordHistory(page);
    await openHeroes(page, '/heroes?lang=en&w=3d', 'threeDay');

    await page.getByRole('button', { name: 'Non-Legend' }).click();
    await expect(page).toHaveURL(/\/heroes\?lang=en&w=3d&s=non_legend$/);
    expect(await historyActions(page)).toEqual([
      { mode: 'replace', href: '/heroes?lang=en&w=3d&s=non_legend' },
    ]);

    // Only Non-Legend rows rank with data; Legend-only heroes trail with zero runs.
    expect((await rankingHeroes(page)).slice(0, 3).toSorted()).toEqual([
      'Dooley',
      'Mak',
      'Vanessa',
    ]);
    await expect(
      rankingTable(page)
        .locator('tbody tr')
        .filter({ has: page.locator('[data-hero-badge="Jules"]') })
    ).toContainText('—');
    await expect(page.locator('[data-testid="daily-winrate-line"][data-hero="Mak"]')).toHaveCount(
      1
    );

    await rankingTable(page).getByRole('button', { name: 'Mak' }).click();
    await expect(page.getByTestId('selected-matchup-hero')).toContainText('Mak');
    await expect(page.getByTestId('matchup-list')).toContainText('Stelle');
  });

  test('a three-day snapshot fills 7D with its real days instead of inventing four', async ({
    page,
  }) => {
    await openHeroes(page, '/heroes?lang=en&w=3d', 'threeDay');
    const chart = page.getByTestId('daily-winrate-chart');
    await expect(chart.getByText('Jun 4', { exact: true })).toBeVisible();

    await page.getByRole('button', { name: '1D' }).click();
    await expect(page).toHaveURL(/\/heroes\?lang=en$/);
    await expect(chart.getByText('Jun 4', { exact: true })).toBeVisible();

    await page.getByRole('button', { name: '7D' }).click();
    await expect(page).toHaveURL(/\/heroes\?lang=en&w=7d$/);
    await expect(chart.getByText('Jun 4', { exact: true })).toBeVisible();
    await expect(chart.getByText('Jun 6', { exact: true })).toBeVisible();
    await expect(chart.getByText(/^Jun [1-3]$/)).toHaveCount(0);
  });

  test('trend gaps stay gaps and points keep their tooltip', async ({ page }) => {
    await openHeroes(page, '/heroes?lang=en&w=3d&s=legend', 'threeDay');
    const vanessa = page.locator('[data-testid="daily-winrate-line"][data-hero="Vanessa"]');

    await expect(vanessa.locator('circle')).toHaveCount(2);
    await expect(vanessa.locator('polyline:not([stroke="transparent"])')).toHaveCount(2);

    await rankingTable(page).getByRole('button', { name: 'Vanessa' }).click();
    await expect(page.getByText('Some days have no calculable trend point.')).toBeVisible();
    await vanessa.locator('circle').first().focus();
    await expect(page.getByTestId('trend-point-tooltip')).toBeVisible();
  });

  test('matchups are ordered, follow the focused hero, and tag low samples', async ({ page }) => {
    await openHeroes(page, '/heroes?lang=en&w=3d&s=legend', 'threeDay');
    const list = page.getByTestId('matchup-list');

    await expect(page.getByTestId('selected-matchup-hero')).toContainText('Jules');
    expect(
      await list
        .locator('[data-hero-badge]')
        .evaluateAll((badges) => badges.map((badge) => badge.getAttribute('data-hero-badge')))
    ).toEqual(['Stelle', 'Jules', 'Vanessa', 'Dooley']);
    await expect(list.getByText('70.0%')).toBeVisible();
    await expect(list.getByText('low sample')).toBeVisible();
    await expect(list.getByText(/15 battles/)).toBeVisible();

    await rankingTable(page).getByRole('button', { name: 'Stelle' }).click();
    await expect(page.getByTestId('selected-matchup-hero')).toContainText('Stelle');
    await expect(list).toContainText('Mak');
  });

  test('every outcome bucket shows, including derived Misfortune, with no battle-day UI', async ({
    page,
  }) => {
    await openHeroes(page, '/heroes?lang=en&w=3d', 'threeDay');
    const table = rankingTable(page);

    await expect(table.getByRole('button', { name: 'Misfortune' })).toBeVisible();
    await expect(table.getByText('10.0%').first()).toBeVisible();
    await expect(page.getByText('Battle win rate')).toHaveCount(0);
    await expect(page.getByText('Day 1', { exact: true })).toHaveCount(0);
  });

  test('a failed day stays explicit while usable days and canonical heroes render', async ({
    page,
  }) => {
    await openHeroes(page, '/heroes?lang=en&w=3d', 'failedDay');

    await expect(
      page.getByText('Some days are unavailable; this view includes loaded days only.')
    ).toBeVisible();
    await expect(rankingTable(page)).toBeVisible();
    // The usable days carry a non-canonical hero; analysis leaves it out everywhere.
    await expect(page.locator('[data-hero="Common"], [data-hero-badge="Common"]')).toHaveCount(0);
  });

  test('a snapshot without usable days renders snapshot unavailable', async ({ page }) => {
    await openHeroes(page, '/heroes?lang=en', 'empty');

    await expect(page.getByRole('heading', { name: 'Snapshot unavailable' })).toBeVisible();
    await expect(page.getByRole('table')).toHaveCount(0);
  });
});

/** Reads one column's text per row, in rendered order. */
async function column(page: Page, index: number): Promise<string[]> {
  return rankingTable(page)
    .locator(`tbody tr td:nth-child(${index + 1})`)
    .evaluateAll((cells) => cells.map((cell) => (cell.textContent ?? '').trim()));
}

function expectNullsLast(values: string[], direction: 'asc' | 'desc') {
  const firstNull = values.indexOf('—');
  const present = firstNull === -1 ? values : values.slice(0, firstNull);
  expect(values.slice(present.length).every((value) => value === '—')).toBe(true);
  const numbers = present.map(Number.parseFloat);
  const sorted = [...numbers].sort((a, b) => (direction === 'asc' ? a - b : b - a));
  expect(numbers).toEqual(sorted);
}

test.describe('ranking table', () => {
  test('header clicks sort with nulls last, and a row click moves the focus', async ({ page }) => {
    await openHeroes(page, '/heroes?lang=en&s=legend');
    const table = rankingTable(page);

    await expect(table.getByRole('columnheader', { name: /10W rate/ })).toHaveAttribute(
      'aria-sort',
      'descending'
    );
    expectNullsLast(await column(page, 1), 'desc');

    await table.getByRole('button', { name: 'Hero' }).click();
    const ascending = await rankingHeroes(page);
    expect(ascending).toEqual([...ascending].sort((a, b) => a.localeCompare(b)));
    await table.getByRole('button', { name: 'Hero' }).click();
    expect(await rankingHeroes(page)).toEqual([...ascending].reverse());

    await table.getByRole('button', { name: /Avg days/ }).click();
    expectNullsLast(await column(page, 5), 'asc');
    await table.getByRole('button', { name: /Avg days/ }).click();
    expectNullsLast(await column(page, 5), 'desc');

    const before = await page.getByTestId('selected-matchup-hero').getAttribute('aria-label');
    const target = (await rankingHeroes(page))[0];
    expect(before).not.toMatch(new RegExp(`: ${target}$`));
    await table.getByRole('button', { name: target }).click();
    await expect(page.getByTestId('selected-matchup-hero')).toHaveAttribute(
      'aria-label',
      new RegExp(`: ${target}$`)
    );
  });
});
