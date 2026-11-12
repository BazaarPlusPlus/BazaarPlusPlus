import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { afterEach, expect, test } from 'vite-plus/test';
import { WORKSPACE_ROOT } from './product.ts';
import {
  GAME_LIBS_LOCK_PATH,
  GAME_VERSION_PATTERN,
  PUBLISHER_KEY,
  gameVersionKey,
  interopSnapshotName,
  lockKey,
  lockKeys,
  lockWarnings,
  lockedEntries,
  parseGameVersion,
  readGameLibsLock,
  validateGameLibsLock
} from './snapshot-lock.ts';
import type { GameLibsLock, GameLibsLockEntries } from './snapshot-lock.ts';

// The Snapshot Lock (root GLOSSARY.md, ADR 0004 and ADR 0008): its keys, the
// game version that names an entry, validation of every entry shape, and the
// warnings that never fail a gate. Capture, publish and fetch are tested in
// game-libs.test.ts and game-libs-interop.test.ts.

const roots: string[] = [];
afterEach(() => {
  for (const root of roots.splice(0))
    fs.rmSync(root, { recursive: true, force: true });
});

const STAGING_VERSION = '1.0.12575-staging-macos-arm64-fecb8f8e';
const ONLINE_VERSION = '1.0.12600-macos-arm64-0a1b2c3d';
const ANDROID_VERSION = '1.0.12825-production-android-d9601ded';
const UNITY_VERSION = '6000.3.11f1';
const NOW = new Date('2026-10-03T12:00:00.000Z');

function emptyLock(): GameLibsLock {
  return {
    schemaVersion: 1,
    staleAfterDays: 30,
    entries: Object.fromEntries(
      lockKeys().map((key) => [key, null])
    ) as GameLibsLockEntries
  };
}

// A workspace that holds only an empty lock.
function workspace() {
  const workspaceRoot = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-lock-ws-'));
  roots.push(workspaceRoot);
  const file = path.join(workspaceRoot, GAME_LIBS_LOCK_PATH);
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, `${JSON.stringify(emptyLock(), null, 2)}\n`);
  return { workspaceRoot };
}

test('the committed lock validates and names every platform-channel key', () => {
  const lock = readGameLibsLock(WORKSPACE_ROOT);
  expect(Object.keys(lock.entries)).toEqual([
    'macos-online',
    'macos-staging',
    'macos-ptr',
    'windows-online',
    'windows-staging',
    'windows-ptr',
    'android-publisher',
    'android-play'
  ]);
  expect(lock.staleAfterDays).toBe(30);
});

test('game versions carry their platform and channel', () => {
  expect(parseGameVersion(STAGING_VERSION)).toEqual({
    platform: 'macos',
    channel: 'staging'
  });
  expect(parseGameVersion('1.0.11358-ptr-windows-x64-947c079a')).toEqual({
    platform: 'windows',
    channel: 'ptr'
  });
  expect(parseGameVersion(ONLINE_VERSION).channel).toBe('online');
  expect(() => parseGameVersion('25661913')).toThrow(/Application.version/);
});

test('lock validation rejects every malformed shape it will meet', () => {
  const entry = {
    gameVersion: STAGING_VERSION,
    sha256: 'a'.repeat(64),
    buildid: '25661913',
    steamBranch: 'staging',
    capturedAt: '2026-10-03T00:00:00.000Z'
  };
  const withEntry = (key: string, value: object) => {
    const lock = emptyLock();
    (lock.entries as Record<string, unknown>)[key] = value;
    return lock;
  };
  expect(validateGameLibsLock(withEntry('macos-staging', entry))).toBeTruthy();
  const cases: [object, RegExp][] = [
    [{ ...emptyLock(), schemaVersion: 2 }, /schemaVersion/],
    [{ ...emptyLock(), staleAfterDays: 0 }, /staleAfterDays/],
    [{ ...emptyLock(), extra: true }, /unknown top-level/],
    [
      { ...emptyLock(), entries: { 'macos-online': null } },
      /exactly the keys macos-online, .*, android-publisher, android-play/
    ],
    [withEntry('macos-staging', { ...entry, note: 'x' }), /exactly the fields/],
    [withEntry('macos-online', entry), /is a macos-staging build/],
    [withEntry('windows-staging', entry), /is a macos-staging build/],
    [withEntry('macos-staging', { ...entry, sha256: 'xyz' }), /sha256/],
    [withEntry('macos-staging', { ...entry, buildid: 25661913 }), /buildid/],
    [
      withEntry('macos-staging', { ...entry, steamBranch: 'public' }),
      /steamBranch must be staging/
    ],
    [
      withEntry('macos-staging', { ...entry, capturedAt: '2026-10-03' }),
      /capturedAt/
    ]
  ];
  for (const [lock, message] of cases)
    expect(() => validateGameLibsLock(lock), JSON.stringify(lock)).toThrow(
      message
    );
});

test('warnings name empty and stale entries without failing', () => {
  const lock = emptyLock();
  lock.entries['macos-staging'] = {
    gameVersion: STAGING_VERSION,
    sha256: 'a'.repeat(64),
    buildid: '25661913',
    steamBranch: 'staging',
    capturedAt: '2026-08-01T00:00:00.000Z'
  };
  lock.entries['windows-online'] = {
    gameVersion: '1.0.12600-windows-x64-0a1b2c3d',
    sha256: 'b'.repeat(64),
    buildid: '25661999',
    steamBranch: 'public',
    capturedAt: '2026-10-01T00:00:00.000Z'
  };
  const warnings = lockWarnings(lock, { now: NOW });
  expect(warnings).toHaveLength(7);
  expect(
    warnings.filter((w) => w.includes('no snapshot captured'))
  ).toHaveLength(4);
  expect(warnings.find((w) => w.startsWith('android-play'))).toMatch(
    /no Interop Snapshot captured; run 'just mod::snapshot-android <APK>\.\.\.', then 'just mod::publish android play'/
  );
  expect(warnings.find((w) => w.startsWith('macos-staging'))).toMatch(
    /captured 63 days ago \(staleAfterDays 30\)/
  );
  expect(warnings.some((w) => w.startsWith('windows-online'))).toBe(false);
  expect(lockedEntries(lock).map(({ key }) => key)).toEqual([
    'macos-staging',
    'windows-online'
  ]);
});

test('an Android version names its source: the publisher form or a bare Play versionName', () => {
  expect(parseGameVersion(ANDROID_VERSION)).toEqual({
    platform: 'android',
    channel: 'online'
  });
  expect(gameVersionKey(ANDROID_VERSION)).toBe('android-publisher');
  expect(gameVersionKey('1.0.6')).toBe('android-play');
  expect(gameVersionKey(STAGING_VERSION)).toBe('macos-staging');
  expect(() => gameVersionKey('1.0.12825-staging-android-d9601ded')).toThrow(
    /Android staging build; the lock holds only production builds/
  );
  expect(() => gameVersionKey('1.0')).toThrow(/Application.version/);
  // An Interop Snapshot is named by versionCode: a Play versionName may name
  // more than one build.
  expect(interopSnapshotName('play', 13168)).toBe('android-play-13168');
  expect(parseGameVersion('1.0.12651-prod-macos-arm64-d5cc730f')).toEqual({
    platform: 'macos',
    channel: 'online'
  });
  expect(() => parseGameVersion('1.0.12825-production-ios-d9601ded')).toThrow(
    /Application.version/
  );
  // Desktop keeps exactly one architecture token; Android has none.
  expect(() => parseGameVersion('1.0.12651-prod-macos-d5cc730f')).toThrow(
    /Application.version/
  );
  expect(() =>
    parseGameVersion('1.0.12825-production-android-arm64-d9601ded')
  ).toThrow(/Application.version/);
  expect(lockKeys().slice(-2)).toEqual(['android-publisher', 'android-play']);
  expect(() => lockKey('android', 'online')).toThrow(
    /Unknown android snapshot channel 'online'; expected publisher, play/
  );
});

test('build/ManagedPath.props mirrors the game version pattern', () => {
  expect(
    execFileSync(
      'dotnet',
      [
        'msbuild',
        path.join(WORKSPACE_ROOT, 'bazaarplusplus-mod/build/ManagedPath.props'),
        '-nologo',
        '-getProperty:BppGameVersionPattern'
      ],
      { encoding: 'utf8' }
    ).trim()
  ).toBe(GAME_VERSION_PATTERN.source);
}, 30000);

test('lock validation holds an Android entry to its schema and field order', () => {
  const ws = workspace();
  const entry = {
    gameVersion: ANDROID_VERSION,
    versionCode: 12825,
    apkSha256: 'a'.repeat(64),
    apkEtag: '72439fcc9794ac65abd8649b18aa59ac',
    splitApkSha256s: [] as string[],
    il2cppSha256: 'b'.repeat(64),
    metadataSha256: 'c'.repeat(64),
    unityVersion: UNITY_VERSION,
    interopSha256: 'd'.repeat(64),
    loader: {
      tag: 'v0.7.4-android.4',
      sha256: 'e'.repeat(64),
      managedRuntimeVersion: '11.0.0'
    },
    patcher: { tag: 'v2.1.0', sha256: 'f'.repeat(64) },
    cpp2ilVersion: '2022.1.0-pre-release.21',
    cpp2ilSha256: '1'.repeat(64),
    il2cppInteropVersion: '1.5.3.0',
    nativeLibs: {},
    capturedAt: NOW.toISOString()
  };
  const lock = (value: object, key: string = PUBLISHER_KEY) => {
    const parsed = readGameLibsLock(ws.workspaceRoot);
    (parsed.entries as Record<string, unknown>)[key] = value;
    return parsed;
  };
  expect(validateGameLibsLock(lock(entry))).toBeTruthy();
  const play = {
    ...entry,
    gameVersion: '1.0.6',
    versionCode: 13168,
    apkEtag: null,
    splitApkSha256s: ['2'.repeat(64), '3'.repeat(64)]
  };
  expect(validateGameLibsLock(lock(play, 'android-play'))).toBeTruthy();
  expect(() => validateGameLibsLock(lock(play))).toThrow(
    /Lock entry android-publisher names game version 1\.0\.6, which is a android-play build/
  );
  expect(() => validateGameLibsLock(lock(entry, 'android-play'))).toThrow(
    /which is a android-publisher build/
  );
  const { gameVersion, ...rest } = entry;
  const cases: [object, RegExp][] = [
    [{ ...rest, gameVersion }, /in the order gameVersion, versionCode/],
    [{ ...entry, sha256: 'a'.repeat(64) }, /exactly the fields/],
    [
      { ...entry, gameVersion: '1.0.12651-prod-macos-arm64-d5cc730f' },
      /is a macos-online build/
    ],
    [{ ...entry, versionCode: '12825' }, /versionCode/],
    [{ ...entry, apkSha256: 'xyz' }, /invalid apkSha256/],
    [
      { ...entry, apkEtag: '"quoted"' },
      /apkEtag must be null or a strong ETag/
    ],
    [{ ...entry, apkEtag: '' }, /apkEtag must be null or a strong ETag/],
    [
      { ...entry, splitApkSha256s: ['3'.repeat(64), '2'.repeat(64)] },
      /splitApkSha256s must be the split APKs' sha256 values, sorted and distinct/
    ],
    [
      { ...entry, splitApkSha256s: ['xyz'] },
      /splitApkSha256s must be the split APKs' sha256 values/
    ],
    [{ ...entry, unityVersion: '6000.3' }, /unityVersion/],
    [
      { ...entry, loader: { tag: 'v0.7.4', sha256: 'e'.repeat(64) } },
      /loader must have exactly the fields tag, sha256, managedRuntimeVersion/
    ],
    [
      { ...entry, patcher: { tag: '../x', sha256: 'f'.repeat(64) } },
      /patcher\.tag must be a version or tag token/
    ],
    [
      { ...entry, nativeLibs: { 'sqlite.dll': {} } },
      /nativeLibs key "sqlite\.dll"/
    ],
    [{ ...entry, capturedAt: '2026-10-22' }, /capturedAt/]
  ];
  for (const [value, message] of cases)
    expect(
      () => validateGameLibsLock(lock(value)),
      JSON.stringify(value)
    ).toThrow(message);
});
