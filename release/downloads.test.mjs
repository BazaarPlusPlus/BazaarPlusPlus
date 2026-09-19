import { expect, test } from 'vitest';
import {
  buildMainlandDownloadUrl,
  DOWNLOAD_PLATFORM_KEYS
} from './downloads.ts';
import { RELEASE_PLATFORM_KEYS } from './release-platforms.mjs';

test.each([
  ['windows', '3.1.1', 'https://cauyxy.lanzout.com/bppwin311'],
  ['mac', '3.1.1', 'https://cauyxy.lanzout.com/bppmac311'],
  ['windows', '10.20.30', 'https://cauyxy.lanzout.com/bppwin102030'],
  ['mac', '10.20.30', 'https://cauyxy.lanzout.com/bppmac102030']
])(
  'mainland download for %s %s uses its versioned mirror URL',
  (platform, version, expected) => {
    expect(buildMainlandDownloadUrl(platform, version)).toBe(expected);
  }
);

test('browser download platforms cover the declared Product Release platforms', () => {
  expect(Object.values(DOWNLOAD_PLATFORM_KEYS)).toEqual(RELEASE_PLATFORM_KEYS);
});
