import fs from 'node:fs';
import path from 'node:path';
import { expect, test } from 'vitest';
import {
  WORKSPACE_ROOT,
  assertProductVersion,
  RELEASE_BASE_URL
} from './product.mjs';
import {
  RELEASE_PLATFORMS,
  RELEASE_PLATFORM_KEYS
} from './release-platforms.mjs';

const fixture = JSON.parse(
  fs.readFileSync(new URL('./fixtures/latest.json', import.meta.url), 'utf8')
);

test('shared fixture supplies the static Tauri updater release metadata', () => {
  expect(assertProductVersion(fixture.version)).toBe(fixture.version);
  expect(typeof fixture.notes).toBe('string');
  // RemoteRelease deserializes pub_date as RFC 3339 when present.
  expect(fixture.pub_date).toMatch(
    /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})$/
  );
  expect(new Date(fixture.pub_date).toISOString()).toBe(fixture.pub_date);
  expect(Object.keys(fixture.platforms).sort()).toEqual(
    [...RELEASE_PLATFORM_KEYS].sort()
  );
  const config = JSON.parse(
    fs.readFileSync(
      path.join(
        WORKSPACE_ROOT,
        'bazaarplusplus-installer/src-tauri/tauri.conf.json'
      ),
      'utf8'
    )
  );
  expect(config.plugins.updater.endpoints).toEqual([
    `${RELEASE_BASE_URL}/latest.json`
  ]);
});

test.each(RELEASE_PLATFORMS)(
  'shared fixture supplies the updater URL and signature for $key',
  ({ key }) => {
    const platform = fixture.platforms[key];
    expect(typeof platform.url).toBe('string');
    const url = new URL(platform.url);
    expect(url.origin).toBe(RELEASE_BASE_URL);
    expect(url.pathname.startsWith(`/${fixture.version}/${key}/updater/`)).toBe(
      true
    );
    expect(typeof platform.signature).toBe('string');
    expect(platform.signature.trim().length).toBeGreaterThan(0);
  }
);
