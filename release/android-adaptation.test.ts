import fs from 'node:fs';
import path from 'node:path';
import { expect, test } from 'vite-plus/test';
import { captureKind } from './android-adaptation.ts';
import { GAME_LIBS_LOCK_PATH, PUBLISHER_KEY } from './snapshot-lock.ts';
import type { InteropLockEntry } from './snapshot-lock.ts';
import { WORKSPACE_ROOT } from './product.ts';

const LOCKED = Object.freeze(
  JSON.parse(
    fs.readFileSync(path.join(WORKSPACE_ROOT, GAME_LIBS_LOCK_PATH), 'utf8')
  ).entries[PUBLISHER_KEY] as InteropLockEntry
);
const NOW = new Date('2026-11-02T03:04:05.000Z');

const captured = (changes: Partial<InteropLockEntry>): InteropLockEntry => ({
  ...LOCKED,
  apkSha256: 'a'.repeat(64),
  capturedAt: NOW.toISOString(),
  ...changes
});

test('the same APKs are unchanged', () => {
  expect(captureKind(LOCKED, { ...LOCKED })).toBe('unchanged');
  expect(
    captureKind(LOCKED, { ...LOCKED, splitApkSha256s: ['b'.repeat(64)] })
  ).toBe('content');
});

test('a new APK with the locked game code is a content-only update', () => {
  expect(
    captureKind(
      LOCKED,
      captured({
        gameVersion: '1.0.12826-production-android-0badc0de',
        versionCode: 12826
      })
    )
  ).toBe('content');
});

test('a new libil2cpp.so or global-metadata.dat is a code change', () => {
  for (const changes of [
    { il2cppSha256: 'b'.repeat(64) },
    { metadataSha256: 'c'.repeat(64) },
    {
      il2cppSha256: 'b'.repeat(64),
      metadataSha256: 'c'.repeat(64),
      interopSha256: 'd'.repeat(64)
    }
  ])
    expect(captureKind(LOCKED, captured(changes))).toBe('code');
  expect(captureKind(null, captured({}))).toBe('code');
});

test('the locked game code with another Interop Snapshot is refused', () => {
  expect(() =>
    captureKind(LOCKED, captured({ interopSha256: 'd'.repeat(64) }))
  ).toThrow(
    `its Interop Snapshot hashes to ${'d'.repeat(64)}, not the locked ${LOCKED.interopSha256}`
  );
  // A recapture of the locked APKs regenerates a timestamped interop.
  expect(
    captureKind(LOCKED, { ...LOCKED, interopSha256: 'd'.repeat(64) })
  ).toBe('unchanged');
});
