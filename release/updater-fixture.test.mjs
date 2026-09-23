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
import {
  DOWNLOAD_PLATFORM_KEYS,
  UPDATER_ENDPOINTS,
  platformManifestPath
} from './downloads.ts';
import { validatePlatformManifest } from './manifest.mjs';

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
  expect(config.plugins.updater.endpoints).toEqual([...UPDATER_ENDPOINTS]);
});

test.each(RELEASE_PLATFORMS)(
  'shared platform fixture for $key is a one-platform Tauri manifest with its own version',
  ({ key }) => {
    const platformFixture = JSON.parse(
      fs.readFileSync(
        new URL(`./fixtures/${platformManifestPath(key)}`, import.meta.url),
        'utf8'
      )
    );
    expect(validatePlatformManifest(platformFixture, key)).toBe(
      platformFixture
    );
    expect(Object.keys(platformFixture.platforms)).toEqual([key]);
    expect(new Date(platformFixture.pub_date).toISOString()).toBe(
      platformFixture.pub_date
    );
    expect(new URL(platformFixture.platforms[key].url).origin).toBe(
      RELEASE_BASE_URL
    );
  }
);

test('the shared platform fixtures let one platform run ahead of the lockstep fixture', () => {
  const versions = Object.fromEntries(
    RELEASE_PLATFORM_KEYS.map((key) => [
      key,
      JSON.parse(
        fs.readFileSync(
          new URL(`./fixtures/${platformManifestPath(key)}`, import.meta.url),
          'utf8'
        )
      ).version
    ])
  );
  expect(versions['darwin-aarch64']).toBe(fixture.version);
  expect(assertProductVersion(versions['windows-x86_64'])).not.toBe(
    fixture.version
  );
});

test.each(RELEASE_PLATFORMS)(
  'shared fixture publishes the installer and its mainland mirror for $key',
  ({ key }) => {
    const download = fixture.downloads[key];
    expect(new URL(download.url).origin).toBe(RELEASE_BASE_URL);
    expect(new URL(download.mainlandUrl).protocol).toBe('https:');
  }
);

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

test('the mod restates the release origin, platform keys and manifest path exactly', () => {
  const source = fs.readFileSync(
    path.join(
      WORKSPACE_ROOT,
      'bazaarplusplus-mod/src/BazaarPlusPlus/Infrastructure/ReleaseManifest/ReleaseManifestEndpoints.cs'
    ),
    'utf8'
  );
  expect(source).toContain(`"${RELEASE_BASE_URL}"`);
  expect(source).toContain(`"${DOWNLOAD_PLATFORM_KEYS.windows}"`);
  expect(source).toContain(`"${DOWNLOAD_PLATFORM_KEYS.mac}"`);
  expect(source).toContain(platformManifestPath('{platformKey}'));
  expect(source).toContain('latest.json');
});
