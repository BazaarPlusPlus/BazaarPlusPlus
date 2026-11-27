import fs from 'node:fs';
import path from 'node:path';
import { DOWNLOAD_PLATFORM_KEYS } from './downloads.ts';
import type { DownloadPlatform } from './downloads.ts';
import { platformInventory } from './payload-inventory.ts';
import type { PayloadHost, PayloadInventory } from './payload-inventory.ts';

// Shipped Tauri clients, the site and the mod request these keys. Only append new keys.
// A platform with multiple Rust triples would require a schema migration.

export type ReleasePlatformKey =
  (typeof DOWNLOAD_PLATFORM_KEYS)[DownloadPlatform];
export type LockstepPlatformKey =
  | typeof DOWNLOAD_PLATFORM_KEYS.windows
  | typeof DOWNLOAD_PLATFORM_KEYS.mac;
export type TauriBuildPlatform = 'windows' | 'macos';
export type BuildPlatform = TauriBuildPlatform | 'android';

export interface TauriReleasePlatform {
  readonly key: ReleasePlatformKey;
  readonly kind: 'tauri';
  readonly buildPlatform: TauriBuildPlatform;
  readonly nodePlatform: NodeJS.Platform;
  readonly payloadHost: PayloadHost;
  readonly rustTarget: string | null;
  readonly bundleRoot: string;
  readonly installerDir: string;
  readonly bundleCleanupDir: string;
  readonly releaseBinary: string;
  readonly installerNameGlob: string;
  readonly bundleTargets: string;
  readonly tauriConfig: string;
  readonly resourceZip: string;
}

export interface AndroidReleasePlatform {
  readonly key: ReleasePlatformKey;
  readonly kind: 'android';
  readonly buildPlatform: 'android';
  readonly nodePlatform: null;
  readonly payloadHost: PayloadHost;
}

export type ReleasePlatform = TauriReleasePlatform | AndroidReleasePlatform;

export const RELEASE_PLATFORMS: readonly ReleasePlatform[] = Object.freeze([
  Object.freeze({
    key: DOWNLOAD_PLATFORM_KEYS.windows,
    kind: 'tauri',
    buildPlatform: 'windows',
    nodePlatform: 'win32',
    payloadHost: 'bepinex',
    rustTarget: null,
    bundleRoot: 'src-tauri/target/release/bundle',
    installerDir: 'src-tauri/target/release/bundle/nsis',
    bundleCleanupDir: 'src-tauri/target/release/bundle/nsis',
    releaseBinary: 'src-tauri/target/release/bppinstaller.exe',
    installerNameGlob: '*.exe',
    bundleTargets: 'nsis',
    tauriConfig: 'src-tauri/tauri.windows.conf.json',
    resourceZip: 'src-tauri/resources/BepInExSource/windows/BepInEx.zip'
  }),
  Object.freeze({
    key: DOWNLOAD_PLATFORM_KEYS.mac,
    kind: 'tauri',
    buildPlatform: 'macos',
    nodePlatform: 'darwin',
    payloadHost: 'bepinex',
    rustTarget: 'aarch64-apple-darwin',
    bundleRoot: 'src-tauri/target/aarch64-apple-darwin/release/bundle',
    installerDir: 'src-tauri/target/aarch64-apple-darwin/release/bundle/dmg',
    bundleCleanupDir: 'src-tauri/target/aarch64-apple-darwin/release/bundle',
    releaseBinary: 'src-tauri/target/aarch64-apple-darwin/release/bppinstaller',
    installerNameGlob: '*.dmg',
    bundleTargets: 'app,dmg',
    tauriConfig: 'src-tauri/tauri.macos.conf.json',
    resourceZip: 'src-tauri/resources/BepInExSource/macos/BepInEx.zip'
  }),
  // Not bound to a host operating system: any machine with .NET 10 and the
  // Android build-tools builds it.
  Object.freeze({
    key: DOWNLOAD_PLATFORM_KEYS.android,
    kind: 'android',
    buildPlatform: 'android',
    nodePlatform: null,
    payloadHost: 'melonloader'
  })
]);

export const RELEASE_PLATFORM_KEYS: readonly ReleasePlatformKey[] =
  Object.freeze(RELEASE_PLATFORMS.map((platform) => platform.key));

// Android follows the game's APK cadence and must never hold back a desktop release (ADR 0012).
export const LOCKSTEP_PLATFORM_KEYS: readonly LockstepPlatformKey[] =
  Object.freeze([DOWNLOAD_PLATFORM_KEYS.windows, DOWNLOAD_PLATFORM_KEYS.mac]);

export function releasePlatform(buildPlatform: string): ReleasePlatform {
  const platform = RELEASE_PLATFORMS.find(
    (candidate) => candidate.buildPlatform === buildPlatform
  );
  if (!platform)
    throw new Error(`Unsupported release platform: ${buildPlatform}`);
  return platform;
}

export function releasePlatformByKey(key: string): ReleasePlatform {
  const platform = RELEASE_PLATFORMS.find((candidate) => candidate.key === key);
  if (!platform) throw new Error(`Unknown release platform: ${key}`);
  return platform;
}

export function tauriPlatform(buildPlatform: string): TauriReleasePlatform {
  const platform = releasePlatform(buildPlatform);
  if (platform.kind !== 'tauri')
    throw new Error(`${buildPlatform} does not ship a Tauri installer`);
  return platform;
}

export function resolveBuildPlatform(
  platformEnv: string | undefined
): TauriBuildPlatform | null {
  return (
    RELEASE_PLATFORMS.find(
      (platform): platform is TauriReleasePlatform =>
        platform.kind === 'tauri' &&
        (platform.buildPlatform === platformEnv ||
          platform.nodePlatform === platformEnv)
    )?.buildPlatform ?? null
  );
}

export function defaultTargetBuildPlatforms(): TauriBuildPlatform[] {
  return RELEASE_PLATFORMS.filter((platform) => platform.kind === 'tauri')
    .map((platform) => platform.buildPlatform)
    .sort();
}

interface TauriConfigOverlay {
  bundle?: { targets?: unknown; resources?: Record<string, unknown> };
}

export function assertPlatformCoherence(rootDir: string): void {
  for (const platform of RELEASE_PLATFORMS) {
    if (platform.kind !== 'tauri') continue;
    const overlayPath = path.join(rootDir, platform.tauriConfig);
    const overlay = JSON.parse(
      fs.readFileSync(overlayPath, 'utf8')
    ) as TauriConfigOverlay;
    const overlayTargets = overlay.bundle?.targets;
    const expectedTargets = platform.bundleTargets.split(',');
    if (JSON.stringify(overlayTargets) !== JSON.stringify(expectedTargets)) {
      throw new Error(
        `${platform.key} bundle targets drifted: ${overlayPath} has ${JSON.stringify(overlayTargets)}, expected ${JSON.stringify(expectedTargets)}`
      );
    }

    const resourceSource = platform.resourceZip.replace(/^src-tauri\//, '');
    if (
      overlay.bundle?.resources?.[resourceSource] !==
      'BepInExSource/BepInEx.zip'
    ) {
      throw new Error(
        `${platform.key} resource ZIP mapping drifted in ${overlayPath}`
      );
    }

    const releaseRoot = platform.rustTarget
      ? `src-tauri/target/${platform.rustTarget}/release`
      : 'src-tauri/target/release';
    if (
      platform.bundleRoot !== `${releaseRoot}/bundle` ||
      !platform.installerDir.startsWith(`${platform.bundleRoot}/`) ||
      !platform.releaseBinary.startsWith(`${releaseRoot}/`)
    ) {
      throw new Error(`${platform.key} target layout is incoherent`);
    }
  }
}

/** bundle.ts signs and team-checks this path. */
export function replayRecorderBundlePath(
  buildPlatform: BuildPlatform,
  inventory?: PayloadInventory
): string {
  const bundles = platformInventory(buildPlatform, inventory).filter(
    (entry) => entry.kind === 'directory' && entry.producer === 'native'
  );
  if (bundles.length !== 1) {
    throw new Error(
      `${buildPlatform} Payload Inventory must name exactly one native plugin bundle; found ${bundles.length}`
    );
  }
  return bundles[0].path;
}
