import { readFileSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';

import { expect, type Page, type Route, type TestInfo } from '@playwright/test';
import Ajv2020 from 'ajv/dist/2020.js';

import { RELEASE_BASE_URL, platformManifestPath } from '../../release/downloads';

export const siteRoot = resolve(import.meta.dirname, '..');

export const METRICS_URL = 'https://bpp-metrics.bazaarplusplus.com/analyzer-v5/heroes/latest.json';
export const SUPPORTERS_URL = 'https://bpp-static.bazaarplusplus.com/supporter-list.json';
export const WINDOWS_MANIFEST_URL = `${RELEASE_BASE_URL}/${platformManifestPath('windows-x86_64')}`;
export const MAC_MANIFEST_URL = `${RELEASE_BASE_URL}/${platformManifestPath('darwin-aarch64')}`;

export function readJson(path: string): unknown {
  return JSON.parse(readFileSync(resolve(siteRoot, path), 'utf8'));
}

const HERO_FIXTURES = {
  // The analyzer owns this golden; it is the exact snapshot the pipeline publishes.
  latest: '../bazaarplusplus-analyzer/contracts/v5/fixtures/heroes.latest.json',
  threeDay: 'e2e/fixtures/heroes-3day.json',
  // Carries a non-canonical hero and one invalid day, so it is not schema-valid on purpose.
  failedDay: 'e2e/fixtures/heroes-failed-day.json',
  empty: 'e2e/fixtures/heroes-empty.json',
} as const;

export type HeroFixture = keyof typeof HERO_FIXTURES;

const validateHeroSnapshot = new Ajv2020({ allErrors: true, strictTypes: false }).compile(
  readJson('../bazaarplusplus-analyzer/contracts/v5/heroes.schema.json') as object
);

/** Validates against the analyzer-owned schema; returns the Ajv errors, or null when valid. */
export function heroSchemaErrors(snapshot: unknown): unknown {
  return validateHeroSnapshot(snapshot) ? null : validateHeroSnapshot.errors;
}

export function heroSnapshot(fixture: HeroFixture): unknown {
  const snapshot = readJson(HERO_FIXTURES[fixture]);
  if (fixture !== 'failedDay') {
    expect(heroSchemaErrors(snapshot), `${fixture} must satisfy heroes.schema.json`).toBeNull();
  }
  return snapshot;
}

/** A canned response, an HTTP status, or `'hang'` to leave the request pending. */
export type SourceReply = { json: unknown } | { status: number } | 'hang';

export type GoldenSources = {
  metrics: SourceReply | (() => SourceReply);
  supporters: SourceReply;
  windows: SourceReply;
  mac: SourceReply;
};

export function goldenSources(overrides: Partial<GoldenSources> = {}): GoldenSources {
  return {
    metrics: { json: heroSnapshot('latest') },
    supporters: { json: readJson('e2e/fixtures/supporters.json') },
    // The workspace release fixtures are read in place, never copied.
    windows: { json: readJson('../release/fixtures/latest/windows-x86_64.json') },
    mac: { json: readJson('../release/fixtures/latest/darwin-aarch64.json') },
    ...overrides,
  };
}

async function reply(route: Route, source: SourceReply): Promise<void> {
  if (source === 'hang') {
    return;
  }
  // Every source is cross-origin: without the CORS header the browser rejects the response.
  const headers = { 'Access-Control-Allow-Origin': '*', 'Content-Type': 'application/json' };
  if ('status' in source) {
    await route.fulfill({ status: source.status, headers, body: '{}' });
    return;
  }
  await route.fulfill({ status: 200, headers, body: JSON.stringify(source.json) });
}

export type GoldenRoutes = {
  /** Non-localhost requests that matched no source and were aborted. */
  unexpected: string[];
  /** How many requests each source received. */
  hits: Record<keyof GoldenSources, number>;
};

/**
 * Serves the three external sources from fixtures and aborts every other non-localhost request,
 * so a source nobody intercepted fails the spec instead of reaching production.
 */
export async function installGoldenRoutes(
  page: Page,
  sources: GoldenSources = goldenSources()
): Promise<GoldenRoutes> {
  const state: GoldenRoutes = {
    unexpected: [],
    hits: { metrics: 0, supporters: 0, windows: 0, mac: 0 },
  };
  const byUrl: Record<string, keyof GoldenSources> = {
    [METRICS_URL]: 'metrics',
    [SUPPORTERS_URL]: 'supporters',
    [WINDOWS_MANIFEST_URL]: 'windows',
    [MAC_MANIFEST_URL]: 'mac',
  };

  await page.route('**/*', async (route) => {
    const url = route.request().url();
    if (new URL(url).host === 'localhost:3000') {
      await route.continue();
      return;
    }
    const key = byUrl[url];
    if (key == null) {
      state.unexpected.push(url);
      await route.abort();
      return;
    }
    state.hits[key] += 1;
    const source = sources[key];
    await reply(route, typeof source === 'function' ? source() : source);
  });
  return state;
}

/** Ends a golden spec: nothing may have tried to leave localhost unintercepted. */
export function expectNoUnexpectedRequests(routes: GoldenRoutes): void {
  expect(routes.unexpected, 'requests outside localhost that no golden source served').toEqual([]);
}

/** Pretty JSON with a trailing newline, the format every JSON golden is committed in. */
export function goldenJson(value: unknown): string {
  return `${JSON.stringify(value, null, 2)}\n`;
}

/** Writes a golden when updating (or when it is missing), and returns the committed value. */
export function readOrWriteGolden(testInfo: TestInfo, name: string, actual: string): string {
  const path = testInfo.snapshotPath(name);
  const update = testInfo.config.updateSnapshots;
  let committed: string | null = null;
  try {
    committed = readFileSync(path, 'utf8');
  } catch {
    committed = null;
  }
  if (committed == null && update === 'none') {
    throw new Error(`${name} golden is missing; regenerate it with BPP_UPDATE_GOLDENS=1`);
  }
  if (committed == null || update === 'all' || update === 'changed') {
    writeFileSync(path, actual);
    return actual;
  }
  return committed;
}
