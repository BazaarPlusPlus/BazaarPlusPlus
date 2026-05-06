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

test('collectVersionSnapshot reads package, tauri, cargo, and cargo lock versions', () => {
  const rootDir = createFixture({
    packageVersion: '2.0.0',
    tauriVersion: '2.0.0',
    cargoVersion: '2.0.0',
    cargoLockVersion: '2.0.0'
  });

  expect(collectVersionSnapshot(rootDir)).toEqual({
    packageVersion: '2.0.0',
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

test('synchronizeVersions updates tauri, cargo, and cargo lock to match package.json', () => {
  const rootDir = createFixture({
    packageVersion: '3.4.5',
    tauriVersion: '1.0.0',
    cargoVersion: '1.0.0',
    cargoLockVersion: '1.0.0'
  });

  const snapshot = synchronizeVersions(rootDir);

  expect(snapshot).toEqual({
    packageVersion: '3.4.5',
    tauriVersion: '3.4.5',
    cargoVersion: '3.4.5',
    cargoLockVersion: '3.4.5'
  });
  expect(collectVersionSnapshot(rootDir)).toEqual(snapshot);
});
