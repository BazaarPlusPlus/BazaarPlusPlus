import fs from 'node:fs';
import path from 'node:path';
import { WORKSPACE_ROOT, readProductVersion } from './product.ts';
import { UPDATER_ENDPOINTS } from './downloads.ts';
import {
  collectVersionSnapshot,
  assertVersionsAreAligned,
  synchronizeVersions
} from './version-sync.ts';
import { synchronizePayloadProjection } from './payload-inventory.ts';
import { assertPlatformCoherence } from './release-platforms.ts';
import { assertWorkspaceHistoryDatabaseCompatibility } from './history-database.ts';
import { lockWarnings, readGameLibsLock } from './snapshot-lock.ts';
import type { ProductVersion } from './product.ts';

// One README's product version badge: the file as read, the version it
// names and the file with the badge projected to VERSION.
interface VersionBadge {
  name: string;
  file: string;
  before: string;
  version: string;
  after: string;
}

/** The options of checkProductProjections. */
export interface CheckProductProjectionsOptions {
  warn?: (message: string) => void;
  now?: Date;
}

// The part of tauri.conf.json the updater endpoint check reads.
interface TauriUpdaterConfig {
  plugins?: { updater?: { endpoints?: unknown } };
}

function readBadges(
  workspaceRoot: string,
  version: ProductVersion
): VersionBadge[] {
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

export function synchronizeProductProjections(
  workspaceRoot: string = WORKSPACE_ROOT
): ProductVersion {
  const version = readProductVersion(workspaceRoot);
  const badges = readBadges(workspaceRoot, version);
  synchronizeVersions(path.join(workspaceRoot, 'bazaarplusplus-installer'));
  synchronizePayloadProjection(workspaceRoot);
  for (const { file, before, after } of badges) {
    if (before !== after) fs.writeFileSync(file, after);
  }
  return version;
}

// `warn` receives the Snapshot Lock's empty and stale entries: they are work
// not yet done, never a failure; a malformed lock throws like any other drift.
export function checkProductProjections(
  workspaceRoot: string = WORKSPACE_ROOT,
  { warn = () => {}, now = new Date() }: CheckProductProjectionsOptions = {}
): ProductVersion {
  const rootDir = path.join(workspaceRoot, 'bazaarplusplus-installer');
  const version = readProductVersion(workspaceRoot);
  assertVersionsAreAligned(collectVersionSnapshot(rootDir));
  synchronizePayloadProjection(workspaceRoot, { check: true });
  assertPlatformCoherence(rootDir);
  assertWorkspaceHistoryDatabaseCompatibility(workspaceRoot);
  for (const warning of lockWarnings(readGameLibsLock(workspaceRoot), { now }))
    warn(warning);
  const config = JSON.parse(
    fs.readFileSync(path.join(rootDir, 'src-tauri/tauri.conf.json'), 'utf8')
  ) as TauriUpdaterConfig;
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
