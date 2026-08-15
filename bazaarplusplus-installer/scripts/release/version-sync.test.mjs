import { test, expect } from 'vitest';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

import {
  assertVersionsAreAligned,
  collectVersionSnapshot,
  synchronizeVersions
} from './version-sync.mjs';

function createFixture({
  packageVersion = '1.2.3',
  packageLockVersion = packageVersion,
  packageLockRootVersion = packageLockVersion,
  tauriVersion = '1.2.3',
  cargoVersion = '1.2.3',
  cargoLockVersion = cargoVersion
} = {}) {
  const rootDir = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-version-sync-'));
  fs.mkdirSync(path.join(rootDir, 'src-tauri'), { recursive: true });

  fs.writeFileSync(
    path.join(rootDir, 'package.json'),
    JSON.stringify({ name: 'bppinstaller', version: packageVersion }, null, 2)
  );
  fs.writeFileSync(
    path.join(rootDir, 'package-lock.json'),
    JSON.stringify(
      {
        name: 'bppinstaller',
        version: packageLockVersion,
        lockfileVersion: 3,
        packages: {
          '': { name: 'bppinstaller', version: packageLockRootVersion }
        }
      },
      null,
      2
    )
  );
  fs.writeFileSync(
    path.join(rootDir, 'src-tauri', 'tauri.conf.json'),
    JSON.stringify({ version: tauriVersion }, null, 2)
  );
  fs.writeFileSync(
    path.join(rootDir, 'src-tauri', 'Cargo.toml'),
    `[package]
name = "bppinstaller"
version = "${cargoVersion}"
`
  );
  fs.writeFileSync(
    path.join(rootDir, 'src-tauri', 'Cargo.lock'),
    `version = 4

[[package]]
name = "bppinstaller"
version = "${cargoLockVersion}"
dependencies = []
`
  );

  return rootDir;
}

test('collectVersionSnapshot reads package, package lock, tauri, cargo, and cargo lock versions', () => {
  const rootDir = createFixture({
    packageVersion: '2.0.0',
    packageLockVersion: '2.0.0',
    tauriVersion: '2.0.0',
    cargoVersion: '2.0.0',
    cargoLockVersion: '2.0.0'
  });

  expect(collectVersionSnapshot(rootDir)).toEqual({
    packageVersion: '2.0.0',
    packageLockVersion: '2.0.0',
    packageLockRootVersion: '2.0.0',
    tauriVersion: '2.0.0',
    cargoVersion: '2.0.0',
    cargoLockVersion: '2.0.0'
  });
});

test('assertVersionsAreAligned throws when versions diverge', () => {
  expect(() =>
    assertVersionsAreAligned({
      packageVersion: '1.1.0',
      tauriVersion: '1.1.0',
      cargoVersion: '1.0.4',
      cargoLockVersion: '1.0.4'
    })
  ).toThrow(/Version mismatch/);
});

test('assertVersionsAreAligned throws when only the package lock is stale', () => {
  expect(() =>
    assertVersionsAreAligned({
      packageVersion: '4.4.2',
      packageLockVersion: '4.3.0',
      tauriVersion: '4.4.2',
      cargoVersion: '4.4.2',
      cargoLockVersion: '4.4.2'
    })
  ).toThrow(/packageLockVersion=4\.3\.0/);
});

test('assertVersionsAreAligned throws when the package lock root package is stale', () => {
  const rootDir = createFixture({
    packageVersion: '4.5.0',
    packageLockVersion: '4.5.0',
    packageLockRootVersion: '4.4.9',
    tauriVersion: '4.5.0',
    cargoVersion: '4.5.0',
    cargoLockVersion: '4.5.0'
  });

  expect(() =>
    assertVersionsAreAligned(collectVersionSnapshot(rootDir))
  ).toThrow(/packageLockRootVersion=4\.4\.9/);
});

test('synchronizeVersions updates package lock, tauri, cargo, and cargo lock to match package.json', () => {
  const rootDir = createFixture({
    packageVersion: '3.4.5',
    packageLockVersion: '1.0.0',
    tauriVersion: '1.0.0',
    cargoVersion: '1.0.0',
    cargoLockVersion: '1.0.0'
  });

  const snapshot = synchronizeVersions(rootDir);

  expect(snapshot).toEqual({
    packageVersion: '3.4.5',
    packageLockVersion: '3.4.5',
    packageLockRootVersion: '3.4.5',
    tauriVersion: '3.4.5',
    cargoVersion: '3.4.5',
    cargoLockVersion: '3.4.5'
  });
  expect(collectVersionSnapshot(rootDir)).toEqual(snapshot);

  const packageLock = JSON.parse(
    fs.readFileSync(path.join(rootDir, 'package-lock.json'), 'utf8')
  );
  expect(packageLock.version).toBe('3.4.5');
  expect(packageLock.packages[''].version).toBe('3.4.5');
});

test('synchronizeVersions preserves Tauri config formatting', () => {
  const rootDir = createFixture({
    packageVersion: '3.4.5',
    tauriVersion: '1.0.0'
  });
  const tauriConfigPath = path.join(rootDir, 'src-tauri', 'tauri.conf.json');
  fs.writeFileSync(
    tauriConfigPath,
    `{
  "version": "1.0.0",
  "plugins": { "updater": { "endpoints": ["https://example.com"] } }
}\n`
  );

  synchronizeVersions(rootDir);

  expect(fs.readFileSync(tauriConfigPath, 'utf8')).toBe(`{
  "version": "3.4.5",
  "plugins": { "updater": { "endpoints": ["https://example.com"] } }
}\n`);
});
