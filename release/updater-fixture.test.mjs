import fs from 'node:fs';
import path from 'node:path';
import { expect, test } from 'vitest';
import {
  WORKSPACE_ROOT,
  assertProductVersion,
  RELEASE_BASE_URL
} from './product.mjs';
import {
  LOCKSTEP_PLATFORM_KEYS,
  RELEASE_PLATFORMS
} from './release-platforms.mjs';
import {
  DOWNLOAD_PLATFORM_KEYS,
  decodeAndroidDownload,
  platformManifestPath
} from './downloads.ts';
import { validatePlatformManifest } from './manifest.mjs';

const fixture = JSON.parse(
  fs.readFileSync(new URL('./fixtures/latest.json', import.meta.url), 'utf8')
);
const LOCKSTEP_PLATFORMS = RELEASE_PLATFORMS.filter(({ key }) =>
  LOCKSTEP_PLATFORM_KEYS.includes(key)
);
const ANDROID = DOWNLOAD_PLATFORM_KEYS.android;
const androidFixture = JSON.parse(
  fs.readFileSync(
    new URL(`./fixtures/${platformManifestPath(ANDROID)}`, import.meta.url),
    'utf8'
  )
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
    [...LOCKSTEP_PLATFORM_KEYS].sort()
  );
});

test.each(LOCKSTEP_PLATFORMS)(
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
    expect(
      platformFixture.platforms[key].url.startsWith(`${RELEASE_BASE_URL}/`)
    ).toBe(true);
  }
);

test('the shared platform fixtures let one platform run ahead of the lockstep fixture', () => {
  const versions = Object.fromEntries(
    LOCKSTEP_PLATFORM_KEYS.map((key) => [
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

test.each(LOCKSTEP_PLATFORMS)(
  'shared fixture publishes the installer and its mainland mirror for $key',
  ({ key }) => {
    const download = fixture.downloads[key];
    expect(download.url.startsWith(`${RELEASE_BASE_URL}/`)).toBe(true);
    expect(new URL(download.mainlandUrl).protocol).toBe('https:');
  }
);

test.each(LOCKSTEP_PLATFORMS)(
  'shared fixture supplies the updater URL and signature for $key',
  ({ key }) => {
    const platform = fixture.platforms[key];
    expect(typeof platform.url).toBe('string');
    expect(
      platform.url.startsWith(
        `${RELEASE_BASE_URL}/${fixture.version}/${key}/updater/`
      )
    ).toBe(true);
    expect(typeof platform.signature).toBe('string');
    expect(platform.signature.trim().length).toBeGreaterThan(0);
  }
);

test('the mod restates the release origin, platform keys and manifest path exactly', () => {
  const source = fs.readFileSync(
    path.join(
      WORKSPACE_ROOT,
      'bazaarplusplus-mod/src/BazaarPlusPlus.Core/Infrastructure/ReleaseManifest/ReleaseManifestEndpoints.cs'
    ),
    'utf8'
  );
  expect(source).toContain(`"${RELEASE_BASE_URL}"`);
  expect(source).toContain(`"${DOWNLOAD_PLATFORM_KEYS.windows}"`);
  expect(source).toContain(`"${DOWNLOAD_PLATFORM_KEYS.mac}"`);
  expect(source).toContain(platformManifestPath('{platformKey}'));
  expect(source).toContain('latest.json');
});

test('the shared Android fixture is a Platform Release Manifest outside latest.json', () => {
  expect(validatePlatformManifest(androidFixture, ANDROID)).toBe(
    androidFixture
  );
  expect(androidFixture.platforms).toBeUndefined();
  expect(Object.keys(fixture.downloads)).not.toContain(ANDROID);
  expect(new Date(androidFixture.pub_date).toISOString()).toBe(
    androidFixture.pub_date
  );
  const download = decodeAndroidDownload(androidFixture);
  expect(download).toEqual({
    version: androidFixture.version,
    apk: androidFixture.downloads[ANDROID].apk,
    managedPayload: androidFixture.downloads[ANDROID].managedPayload,
    game: androidFixture.downloads[ANDROID].game,
    mainlandUrl: null
  });
  // The APK is named by game, loader and interop, not by the product version.
  expect(download.apk.url.startsWith(`${RELEASE_BASE_URL}/`)).toBe(true);
  expect(download.apk.url.slice(RELEASE_BASE_URL.length)).toMatch(
    /^\/android\/apk\/BazaarPlusPlus_\d+\.\d+\.\d+_lemon-[^/]+_[a-f0-9]{8}\.apk$/
  );
  expect(download.apk.url).not.toContain(androidFixture.version);
});
