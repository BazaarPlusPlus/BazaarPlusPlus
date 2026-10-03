import { expect, test } from 'vitest';
import { decodeMainlandDownloadUrl } from './downloads.ts';
import fixture from './fixtures/latest.json' with { type: 'json' };

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
