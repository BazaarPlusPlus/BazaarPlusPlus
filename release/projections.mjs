import fs from 'node:fs';
import path from 'node:path';
import {
  WORKSPACE_ROOT,
  compareProductVersions,
  readProductVersion
} from './product.mjs';
import { UPDATER_ENDPOINTS } from './downloads.ts';
import {
  collectVersionSnapshot,
  assertVersionsAreAligned,
  synchronizeVersions
} from './version-sync.mjs';
import { synchronizePayloadProjection } from './payload-inventory.mjs';
import { assertPlatformCoherence } from './release-platforms.mjs';
import { assertWorkspaceHistoryDatabaseCompatibility } from './history-database.mjs';
import { lockWarnings, readGameLibsLock } from './game-libs.mjs';

function readBadges(workspaceRoot, version) {
  return ['README.md', 'README_en.md'].map((name) => {
    const file = path.join(workspaceRoot, name);
    const before = fs.readFileSync(file, 'utf8');
    const pattern = /img\.shields\.io\/badge\/version-([^-\s/]+)-/g;
    const matches = [...before.matchAll(pattern)];
    if (matches.length !== 1)
      throw new Error(`Expected one product version badge in ${name}`);
    return {
      name,
      file,
      before,
      version: matches[0][1],
      after: before.replace(pattern, `img.shields.io/badge/version-${version}-`)
    };
  });
}

export function synchronizeProductProjections(workspaceRoot = WORKSPACE_ROOT) {
  const version = readProductVersion(workspaceRoot);
  const badges = readBadges(workspaceRoot, version);
  synchronizeVersions(path.join(workspaceRoot, 'bazaarplusplus-installer'));
  synchronizePayloadProjection(workspaceRoot);
  for (const { file, before, after } of badges) {
    if (before !== after) fs.writeFileSync(file, after);
  }
  return version;
}

// Installer ADR 0008: the one-time V5 import ships only before 6.2.0.
const V5_IMPORT_MODULE = 'bazaarplusplus-installer/src-tauri/src/v5_import';
const V5_IMPORT_REMOVED_IN = '6.2.0';

function assertV5ImportRemoved(workspaceRoot, version) {
  if (
    compareProductVersions(version, V5_IMPORT_REMOVED_IN) >= 0 &&
    fs.existsSync(path.join(workspaceRoot, V5_IMPORT_MODULE))
  )
    throw new Error(
      `${V5_IMPORT_MODULE}/ must be deleted before ${V5_IMPORT_REMOVED_IN} (VERSION is ${version}); see https://github.com/BazaarPlusPlus/BazaarPlusPlus/issues/264 and bazaarplusplus-installer/docs/adr/0008-legacy-data-roots-and-v5-import.md`
    );
}

// `warn` receives the Snapshot Lock's empty and stale entries: they are work
// not yet done, never a failure; a malformed lock throws like any other drift.
export function checkProductProjections(
  workspaceRoot = WORKSPACE_ROOT,
  { warn = () => {}, now = new Date() } = {}
) {
  const rootDir = path.join(workspaceRoot, 'bazaarplusplus-installer');
  const version = readProductVersion(workspaceRoot);
  assertV5ImportRemoved(workspaceRoot, version);
  assertVersionsAreAligned(collectVersionSnapshot(rootDir));
  synchronizePayloadProjection(workspaceRoot, { check: true });
  assertPlatformCoherence(rootDir);
  assertWorkspaceHistoryDatabaseCompatibility(workspaceRoot);
  for (const warning of lockWarnings(readGameLibsLock(workspaceRoot), { now }))
    warn(warning);
  const config = JSON.parse(
    fs.readFileSync(path.join(rootDir, 'src-tauri/tauri.conf.json'), 'utf8')
  );
  if (
    JSON.stringify(config.plugins?.updater?.endpoints) !==
    JSON.stringify([...UPDATER_ENDPOINTS])
  )
    throw new Error(
      `Tauri updater endpoints must equal ${JSON.stringify([...UPDATER_ENDPOINTS])}`
    );
  for (const badge of readBadges(workspaceRoot, version)) {
    if (badge.version !== version)
      throw new Error(
        `${badge.name} version badge is ${badge.version}; expected ${version}. Run just release::sync.`
      );
  }
  return version;
}
