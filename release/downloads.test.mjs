import { expect, test } from 'vitest';
import {
  decodeMainlandDownloadUrl,
  DOWNLOAD_PLATFORM_KEYS,
  RELEASE_BASE_URL,
  RELEASE_MANIFEST_PATH,
  UPDATER_ENDPOINTS,
  platformManifestPath
} from './downloads.ts';
import { RELEASE_PLATFORM_KEYS } from './release-platforms.mjs';
import fixture from './fixtures/latest.json' with { type: 'json' };

test('browser download platforms cover the declared Product Release platforms', () => {
  expect(Object.values(DOWNLOAD_PLATFORM_KEYS)).toEqual(RELEASE_PLATFORM_KEYS);
});

test('the updater endpoint template names each platform manifest and falls back to the lockstep manifest', () => {
  for (const key of RELEASE_PLATFORM_KEYS) {
    expect(UPDATER_ENDPOINTS[0].replace('{{target}}-{{arch}}', key)).toBe(
      `${RELEASE_BASE_URL}/${platformManifestPath(key)}`
    );
  }
  expect(UPDATER_ENDPOINTS[1]).toBe(
    `${RELEASE_BASE_URL}/${RELEASE_MANIFEST_PATH}`
  );
  expect(UPDATER_ENDPOINTS).toHaveLength(2);
});

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
