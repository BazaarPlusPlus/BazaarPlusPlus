import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { afterEach, expect, test } from 'vitest';
import { captureKind, cliMain } from './android-adaptation.ts';
import { GAME_LIBS_LOCK_PATH, INTEROP_KEY } from './game-libs.ts';
import type { GameLibsLock, InteropLockEntry } from './game-libs.ts';
import { WORKSPACE_ROOT } from './product.ts';

// android-apk-watch.yml's sorting of a capture, simulated locally: a capture
// is a lock entry beside the previous one.

const LOCKED = Object.freeze(
  JSON.parse(
    fs.readFileSync(path.join(WORKSPACE_ROOT, GAME_LIBS_LOCK_PATH), 'utf8')
  ).entries[INTEROP_KEY] as InteropLockEntry
);
const NOW = new Date('2026-11-02T03:04:05.000Z');

const roots: string[] = [];
afterEach(() => {
  for (const root of roots.splice(0))
    fs.rmSync(root, { recursive: true, force: true });
});
const tempDir = () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-adaptation-'));
  roots.push(root);
  return root;
};
const write = (file: string, bytes: string): string => {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, bytes);
  return file;
};

// A captured android-online entry: the locked one with the given changes.
const captured = (changes: Partial<InteropLockEntry>): InteropLockEntry => ({
  ...LOCKED,
  apkSha256: 'a'.repeat(64),
  capturedAt: NOW.toISOString(),
  ...changes
});

test('the same APK is unchanged', () => {
  expect(captureKind(LOCKED, { ...LOCKED })).toBe('unchanged');
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
});

// A workspace whose lock holds `entry`, as a capture leaves it.
function capturedWorkspace(entry = captured({})) {
  const workspaceRoot = tempDir();
  const lock = JSON.parse(
    fs.readFileSync(path.join(WORKSPACE_ROOT, GAME_LIBS_LOCK_PATH), 'utf8')
  ) as GameLibsLock;
  lock.entries[INTEROP_KEY] = entry;
  write(
    path.join(workspaceRoot, GAME_LIBS_LOCK_PATH),
    `${JSON.stringify(lock, null, 2)}\n`
  );
  return { workspaceRoot, lock };
}

test('kind compares the workspace lock with the previous lock file', async () => {
  const { workspaceRoot, lock } = capturedWorkspace();
  const previousFile = (entry: InteropLockEntry) =>
    write(
      path.join(tempDir(), 'previous-lock.json'),
      JSON.stringify({
        ...lock,
        entries: { ...lock.entries, [INTEROP_KEY]: entry }
      })
    );
  const kind = (entry: InteropLockEntry) =>
    cliMain(['kind', '--previous', previousFile(entry)], { workspaceRoot });
  const current = lock.entries[INTEROP_KEY]!;
  expect(await kind(current)).toBe('unchanged');
  expect(await kind({ ...current, apkSha256: 'e'.repeat(64) })).toBe('content');
  expect(await kind({ ...LOCKED, il2cppSha256: 'f'.repeat(64) })).toBe('code');
  await expect(cliMain(['kind'], { workspaceRoot })).rejects.toThrow('Usage');
});
