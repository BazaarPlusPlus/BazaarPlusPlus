import fs from 'node:fs';
import path from 'node:path';
import { WORKSPACE_ROOT, readProductVersion } from './product.mjs';
import { UPDATER_ENDPOINTS } from './downloads.ts';
import {
  collectVersionSnapshot,
  assertVersionsAreAligned,
  synchronizeVersions
} from './version-sync.mjs';
import { synchronizePayloadProjection } from './payload-inventory.mjs';
import { assertPlatformCoherence } from './release-platforms.mjs';

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

export function checkProductProjections(workspaceRoot = WORKSPACE_ROOT) {
  const rootDir = path.join(workspaceRoot, 'bazaarplusplus-installer');
  const version = readProductVersion(workspaceRoot);
  assertVersionsAreAligned(collectVersionSnapshot(rootDir));
  synchronizePayloadProjection(workspaceRoot, { check: true });
  assertPlatformCoherence(rootDir);
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
