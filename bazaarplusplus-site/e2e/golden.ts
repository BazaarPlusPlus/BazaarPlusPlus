import { readFileSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';

import { expect, type Page, type Route, type TestInfo } from '@playwright/test';
import Ajv2020 from 'ajv/dist/2020.js';

import {
  ANDROID_GAME_STATUS_URL,
  GAME_STATUS_URL,
  RELEASE_BASE_URL,
  platformManifestPath
} from '../../release/downloads';

export const siteRoot = resolve(import.meta.dirname, '..');

export const METRICS_URL =
  'https://cdn.bazaarplusplus.com/metrics/v6/heroes/latest.json';
const SUPPORTERS_URL =
  'https://cdn.bazaarplusplus.com/mod-data/supporters/supporter-list.json';
export const WINDOWS_MANIFEST_URL = `${RELEASE_BASE_URL}/${platformManifestPath('windows-x86_64')}`;
export const MAC_MANIFEST_URL = `${RELEASE_BASE_URL}/${platformManifestPath('darwin-aarch64')}`;
export const ANDROID_MANIFEST_URL = `${RELEASE_BASE_URL}/${platformManifestPath('android-aarch64')}`;

export function readJson(path: string): unknown {
  return JSON.parse(readFileSync(resolve(siteRoot, path), 'utf8'));
}

const HERO_FIXTURES = {
  latest: '../bazaarplusplus-analyzer/contracts/v6/fixtures/heroes.latest.json',
  threeDay: 'e2e/fixtures/heroes-3day.json',
  // Carries a non-canonical hero and one invalid day, so it is not schema-valid on purpose.
  failedDay: 'e2e/fixtures/heroes-failed-day.json',
  empty: 'e2e/fixtures/heroes-empty.json'
} as const;

export type HeroFixture = keyof typeof HERO_FIXTURES;

const validateHeroSnapshot = new Ajv2020({
  allErrors: true,
  strictTypes: false
}).compile(
  readJson(
    '../bazaarplusplus-analyzer/contracts/v6/heroes.schema.json'
  ) as object
);

export function heroSchemaErrors(snapshot: unknown): unknown {
  return validateHeroSnapshot(snapshot) ? null : validateHeroSnapshot.errors;
}

export function heroSnapshot(fixture: HeroFixture): unknown {
  const snapshot = readJson(HERO_FIXTURES[fixture]);
  if (fixture !== 'failedDay') {
    expect(
      heroSchemaErrors(snapshot),
      `${fixture} must satisfy heroes.schema.json`
    ).toBeNull();
  }
  return snapshot;
}

export type SourceReply = { json: unknown } | { status: number } | 'hang';

export type GoldenSources = {
  metrics: SourceReply | (() => SourceReply);
  supporters: SourceReply;
  windows: SourceReply;
  mac: SourceReply;
  android: SourceReply;
  gameStatus: SourceReply;
  androidGameStatus: SourceReply;
};

export function goldenSources(
  overrides: Partial<GoldenSources> = {}
): GoldenSources {
  return {
    metrics: { json: heroSnapshot('latest') },
    supporters: { json: readJson('e2e/fixtures/supporters.json') },
    windows: {
      json: readJson('../release/fixtures/latest/windows-x86_64.json')
    },
    mac: { json: readJson('../release/fixtures/latest/darwin-aarch64.json') },
    android: {
      json: readJson('../release/fixtures/latest/android-aarch64.json')
    },
    // Answered only where a spec asks, so the shared page goldens stay free of Steam's clock.
    gameStatus: { status: 404 },
    androidGameStatus: { status: 404 },
    ...overrides
  };
}

async function reply(route: Route, source: SourceReply): Promise<void> {
  if (source === 'hang') {
    return;
  }
  // Every source is cross-origin: without the CORS header the browser rejects the response.
  const headers = {
    'Access-Control-Allow-Origin': '*',
    'Content-Type': 'application/json'
  };
  if ('status' in source) {
    await route.fulfill({ status: source.status, headers, body: '{}' });
    return;
  }
  await route.fulfill({
    status: 200,
    headers,
    body: JSON.stringify(source.json)
  });
}

export type GoldenRoutes = {
  unexpected: string[];
  hits: Record<keyof GoldenSources, number>;
};

/** Abort unmatched external requests to keep golden specs off production. */
export async function installGoldenRoutes(
  page: Page,
  sources: GoldenSources = goldenSources()
): Promise<GoldenRoutes> {
  const state: GoldenRoutes = {
    unexpected: [],
    hits: {
      metrics: 0,
      supporters: 0,
      windows: 0,
      mac: 0,
      android: 0,
      gameStatus: 0,
      androidGameStatus: 0
    }
  };
  const byUrl: Record<string, keyof GoldenSources> = {
    [METRICS_URL]: 'metrics',
    [SUPPORTERS_URL]: 'supporters',
    [WINDOWS_MANIFEST_URL]: 'windows',
    [MAC_MANIFEST_URL]: 'mac',
    [ANDROID_MANIFEST_URL]: 'android',
    [GAME_STATUS_URL]: 'gameStatus',
    [ANDROID_GAME_STATUS_URL]: 'androidGameStatus'
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

export function expectNoUnexpectedRequests(routes: GoldenRoutes): void {
  expect(
    routes.unexpected,
    'requests outside localhost that no golden source served'
  ).toEqual([]);
}

export function goldenJson(value: unknown): string {
  return `${JSON.stringify(value, null, 2)}\n`;
}

export function readOrWriteGolden(
  testInfo: TestInfo,
  name: string,
  actual: string
): string {
  const path = testInfo.snapshotPath(name);
  const update = testInfo.config.updateSnapshots;
  let committed: string | null = null;
  try {
    committed = readFileSync(path, 'utf8');
  } catch {
    committed = null;
  }
  if (committed == null && update === 'none') {
    throw new Error(
      `${name} golden is missing; regenerate it with BPP_UPDATE_GOLDENS=1`
    );
  }
  if (committed == null || update === 'all' || update === 'changed') {
    writeFileSync(path, actual);
    return actual;
  }
  return committed;
}
