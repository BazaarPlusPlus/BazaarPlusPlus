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
  productVersion = packageVersion,
  packageLockVersion = packageVersion,
  packageLockRootVersion = packageLockVersion,
  tauriVersion = '1.2.3',
  cargoVersion = '1.2.3',
  cargoLockVersion = cargoVersion
} = {}) {
  const workspaceRoot = fs.mkdtempSync(
    path.join(os.tmpdir(), 'bpp-version-sync-')
  );
  const rootDir = path.join(workspaceRoot, 'installer');
  fs.writeFileSync(path.join(workspaceRoot, 'VERSION'), productVersion);
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
    productVersion: '2.0.0',
    packageVersion: '2.0.0',
    packageLockVersion: '2.0.0',
    tauriVersion: '2.0.0',
    cargoVersion: '2.0.0',
    cargoLockVersion: '2.0.0'
  });

  expect(collectVersionSnapshot(rootDir)).toEqual({
    productVersion: '2.0.0',
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
      productVersion: '1.1.0',
      packageVersion: '1.1.0',
      tauriVersion: '1.1.0',
      cargoVersion: '1.0.4',
      cargoLockVersion: '1.0.4'
    })
  ).toThrow(/Version mismatch/);
});

test('package.json is a projection, not an independent product version', () => {
  const rootDir = createFixture({
    productVersion: '5.5.0',
    packageVersion: '5.4.0'
  });
  expect(() =>
    assertVersionsAreAligned(collectVersionSnapshot(rootDir))
  ).toThrow(/packageVersion=5.4.0/);
  const snapshot = synchronizeVersions(rootDir);
  expect(snapshot.productVersion).toBe('5.5.0');
  expect(snapshot.packageVersion).toBe('5.5.0');
  expect(() => assertVersionsAreAligned(snapshot)).not.toThrow();
});

test('assertVersionsAreAligned throws when only the package lock is stale', () => {
  expect(() =>
    assertVersionsAreAligned({
      productVersion: '4.4.2',
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

test('synchronizeVersions updates package lock, tauri, cargo, and cargo lock to match the product VERSION', () => {
  const rootDir = createFixture({
    productVersion: '3.4.5',
    packageVersion: '3.4.5',
    packageLockVersion: '1.0.0',
    tauriVersion: '1.0.0',
    cargoVersion: '1.0.0',
    cargoLockVersion: '1.0.0'
  });

  const snapshot = synchronizeVersions(rootDir);

  expect(snapshot).toEqual({
    productVersion: '3.4.5',
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
    productVersion: '3.4.5',
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
