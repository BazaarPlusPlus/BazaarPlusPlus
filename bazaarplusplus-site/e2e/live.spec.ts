import { createHash } from 'node:crypto';
import { mkdirSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';

import { expect, test, type Page } from '@playwright/test';

import { loadHeroMetricsDataset } from '../src/features/heroes/hero-metrics-dataset';
import { METRICS_URL, heroSchemaErrors } from './golden';

/** The card's version, or null while it is unavailable. */
async function cardVersion(page: Page, title: string): Promise<string | null> {
  const version = page
    .locator('article')
    .filter({ has: page.getByRole('heading', { level: 2, name: title, exact: true }) })
    .getByText('Current version', { exact: true })
    .locator('xpath=following-sibling::*[1]');
  await expect(version).not.toHaveClass(/animate-pulse/, { timeout: 30_000 });
  const text = ((await version.textContent()) ?? '').trim();
  return text.startsWith('v') ? text : null;
}

// Live mode reads the production sources and only reports; it is never a golden and never runs
// in CI. It fails only when the snapshot breaks the schema, /heroes errors, or no card loads.
test('live report against the production sources', async ({ page, request }, testInfo) => {
  const response = await request.get(METRICS_URL);
  const body = await response.body();
  const schemaErrors = response.ok() ? heroSchemaErrors(JSON.parse(body.toString('utf8'))) : null;
  const dataset = await loadHeroMetricsDataset(AbortSignal.timeout(30_000));

  await page.goto('/heroes?lang=en');
  const rankingRows = page.getByTestId('hero-ranking-table').locator('tbody tr');
  const errorHeading = page.getByRole('heading', { name: 'Stats are temporarily unavailable' });
  await expect(rankingRows.first().or(errorHeading)).toBeVisible({ timeout: 60_000 });
  const heroesErrored = await errorHeading.isVisible();
  const heroRows = heroesErrored ? 0 : await rankingRows.count();

  await page.goto('/download?lang=en');
  await page.waitForLoadState('networkidle');
  const titles = ['Windows', 'macOS'];
  const versions = Object.fromEntries(
    await Promise.all(titles.map(async (title) => [title, await cardVersion(page, title)] as const))
  );

  const report = {
    generatedAt: new Date().toISOString(),
    metrics: {
      url: METRICS_URL,
      status: response.status(),
      sha256: createHash('sha256').update(body).digest('hex'),
      schemaValid: response.ok() && schemaErrors == null,
      schemaErrors,
    },
    coverage: {
      window: dataset.window,
      requestedDates: dataset.coverage.requestedDates,
      usableDates: dataset.coverage.usableDates,
      failedDates: dataset.coverage.failedDates,
    },
    heroes: { errored: heroesErrored, rankingRows: heroRows },
    download: versions,
  };
  mkdirSync(testInfo.project.outputDir, { recursive: true });
  const path = resolve(testInfo.project.outputDir, 'live-report.json');
  writeFileSync(path, `${JSON.stringify(report, null, 2)}\n`);
  await testInfo.attach('live-report.json', { path, contentType: 'application/json' });

  expect(report.metrics.schemaValid, 'the production snapshot satisfies heroes.schema.json').toBe(
    true
  );
  expect(heroesErrored, '/heroes renders the production snapshot').toBe(false);
  expect(
    Object.values(versions).some((version) => version != null),
    'a download card loads'
  ).toBe(true);
});
