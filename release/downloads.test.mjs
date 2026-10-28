import { expect, test } from 'vitest';
import {
  LEGACY_RELEASE_BASE_URL,
  RELEASE_BASE_URL,
  DOWNLOAD_PLATFORM_KEYS,
  UPDATER_ENDPOINTS,
  modManifestPath,
  platformManifestPath,
  decodeAndroidDownload,
  decodeMainlandDownloadUrl
} from './downloads.ts';
import fixture from './fixtures/latest.json' with { type: 'json' };
import android from './fixtures/latest/android-aarch64.json' with { type: 'json' };

test('consumers read the mainland mirror address from the Release Manifest', () => {
  expect(decodeMainlandDownloadUrl(fixture, 'windows-x86_64')).toBe(
    fixture.downloads['windows-x86_64'].mainlandUrl
  );
  expect(decodeMainlandDownloadUrl(fixture, 'darwin-aarch64')).toBe(
    fixture.downloads['darwin-aarch64'].mainlandUrl
  );
});

test.each([
  ['a manifest without downloads', { version: '3.1.1' }],
  ['a platform without a mirror', { downloads: { 'windows-x86_64': {} } }],
  [
    'an http address',
    { downloads: { 'windows-x86_64': { mainlandUrl: 'http://m.example/a' } } }
  ],
  [
    'an address carrying credentials',
    {
      downloads: {
        'windows-x86_64': { mainlandUrl: 'https://u:p@m.example/a' }
      }
    }
  ],
  [
    'a non-URL',
    { downloads: { 'windows-x86_64': { mainlandUrl: 'bppwin311' } } }
  ],
  ['a non-object manifest', null]
])('%s yields no mirror instead of a guess', (_, manifest) => {
  expect(decodeMainlandDownloadUrl(manifest, 'windows-x86_64')).toBeNull();
});

test('consumers read the Android APK, Managed Payload and game from the Android manifest', () => {
  const record = android.downloads['android-aarch64'];
  expect(decodeAndroidDownload(android)).toEqual({
    version: android.version,
    apk: record.apk,
    managedPayload: record.managedPayload,
    game: record.game,
    mainlandUrl: null
  });
  const mirrored = structuredClone(android);
  mirrored.downloads['android-aarch64'].mainlandUrl = 'https://m.example/apk';
  expect(decodeAndroidDownload(mirrored)?.mainlandUrl).toBe(
    'https://m.example/apk'
  );
});

const brokenAndroid = (mutate) => {
  const manifest = structuredClone(android);
  mutate(manifest.downloads['android-aarch64'], manifest);
  return manifest;
};

test.each([
  ['a desktop manifest', () => fixture],
  [
    'an APK outside the release origin',
    () =>
      brokenAndroid((record) => {
        record.apk.url = 'https://untrusted.example/android/apk/a.apk';
      })
  ],
  [
    'an APK below the cdn origin but outside the release path',
    () =>
      brokenAndroid((record) => {
        record.apk.url = record.apk.url.replace('/installer/', '/');
      })
  ],
  [
    'an APK under the pre-6.0.0 release origin',
    () =>
      brokenAndroid((record) => {
        record.apk.url = record.apk.url.replace(
          RELEASE_BASE_URL,
          LEGACY_RELEASE_BASE_URL
        );
      })
  ],
  [
    'an APK outside android/apk/',
    () =>
      brokenAndroid((record) => {
        record.apk.url = record.managedPayload.url;
      })
  ],
  [
    'a Managed Payload of another version',
    () =>
      brokenAndroid((_, manifest) => {
        manifest.version = '9.9.9';
      })
  ],
  [
    'an unsigned Managed Payload',
    () =>
      brokenAndroid((record) => {
        record.managedPayload.signature = ' ';
      })
  ],
  [
    'a missing game version code',
    () =>
      brokenAndroid((record) => {
        delete record.game.versionCode;
      })
  ],
  ['a non-object manifest', () => null]
])('%s yields no Android download', (_, manifest) => {
  expect(decodeAndroidDownload(manifest())).toBeNull();
});

test('mod endpoints are separate while installer and website endpoints stay compatible', () => {
  for (const key of Object.values(DOWNLOAD_PLATFORM_KEYS)) {
    expect(modManifestPath(key)).toBe(`latest/mod/${key}.json`);
    expect(platformManifestPath(key)).toBe(`latest/${key}.json`);
  }
  expect(UPDATER_ENDPOINTS).toEqual([
    `${RELEASE_BASE_URL}/latest/{{target}}-{{arch}}.json`,
    `${RELEASE_BASE_URL}/latest.json`
  ]);
});
