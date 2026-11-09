import fs from 'node:fs';
import { expect, test } from 'vite-plus/test';

import { ANDROID_RELEASE_SIGNER_SHA256 } from './manifest.ts';

// The Android App's SharedReleaseFixturesTest reads this declaration from the
// source text with the same pattern, so a type annotation or a quote change
// here breaks that consumer.
test('the Android release signer stays readable from the source text', () => {
  const source = fs.readFileSync(
    new URL('./manifest.ts', import.meta.url),
    'utf8'
  );
  const match = /ANDROID_RELEASE_SIGNER_SHA256 =\s*'([0-9a-f]{64})'/.exec(
    source
  );
  expect(match?.[1]).toBe(ANDROID_RELEASE_SIGNER_SHA256);
});
