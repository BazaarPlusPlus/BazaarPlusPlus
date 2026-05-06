import fs from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const platformAliases = new Map([
  ['darwin', 'macos'],
  ['macos', 'macos'],
  ['win32', 'windows'],
  ['windows', 'windows']
]);

export function resolveBundleCleanupPath(rootDir, platformEnv) {
  const platform = platformAliases.get(platformEnv);
  if (platform === 'macos') {
    return path.join(
      rootDir,
      'src-tauri',
      'target',
      'aarch64-apple-darwin',
      'release',
      'bundle'
    );
  }

  if (platform === 'windows') {
    return path.join(rootDir, 'src-tauri', 'target', 'release', 'bundle', 'nsis');
  }

  return null;
}

export function cleanupBundleArtifacts(rootDir, platformEnv) {
  const cleanupPath = resolveBundleCleanupPath(rootDir, platformEnv);
  if (!cleanupPath) {
    return { cleanupPath: null, removed: false };
  }

  if (!fs.existsSync(cleanupPath)) {
    return { cleanupPath, removed: false };
  }

  fs.rmSync(cleanupPath, { force: true, recursive: true });
  return { cleanupPath, removed: true };
}

function main() {
  const rootDir = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
  const platformEnv = process.env.TAURI_ENV_PLATFORM ?? process.platform;
  const { cleanupPath, removed } = cleanupBundleArtifacts(rootDir, platformEnv);

  if (!cleanupPath) {
    console.log(`before-bundle-cleanup: skipping unsupported platform ${platformEnv}`);
    return;
  }

  if (removed) {
    console.log(`before-bundle-cleanup: removed stale bundle artifacts at ${cleanupPath}`);
    return;
  }

  console.log(`before-bundle-cleanup: no stale bundle artifacts at ${cleanupPath}`);
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  main();
}
