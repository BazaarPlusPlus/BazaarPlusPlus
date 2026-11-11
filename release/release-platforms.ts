import fs from 'node:fs';
import path from 'node:path';
import { DOWNLOAD_PLATFORM_KEYS } from './downloads.ts';
import type { DownloadPlatform } from './downloads.ts';
import { platformInventory } from './payload-inventory.ts';
import type { PayloadHost, PayloadInventory } from './payload-inventory.ts';

// Release platform facts live here so bundle.sh and the Bun release scripts
// cannot drift independently. Browser-safe keys come from downloads.ts.
// `key` values are an external wire contract: they are the Tauri updater
// {{target}}-{{arch}} lookup keys inside every manifest, and the file name of
// each Platform Release Manifest (`platformManifestPath` and
// `UPDATER_ENDPOINTS` in downloads.ts) that shipped installers, the site and
// the mod request. NEVER rename a key; only append. `kind` says how the
// platform ships: `tauri` platforms ship a Tauri installer and updater package
// and carry the Tauri fields below; the `android` platform ships the Android
// App, an Interop Package and a Managed Payload zip and has none of them
// (ADR 0012).
// `payloadHost` names the mod loader whose Payload Inventory entries the
// platform installs. `rustTarget` is scalar-or-null by design. A future
// platform requiring multiple Rust triples needs a schema migration, not a
// comma hack.

/** The wire key of a release platform, e.g. `windows-x86_64`. */
export type ReleasePlatformKey =
  (typeof DOWNLOAD_PLATFORM_KEYS)[DownloadPlatform];
/** The wire key of a Lockstep Platform: one the Release Manifest waits for. */
export type LockstepPlatformKey =
  | typeof DOWNLOAD_PLATFORM_KEYS.windows
  | typeof DOWNLOAD_PLATFORM_KEYS.mac;
/** The build platform name of a platform that ships a Tauri installer. */
export type TauriBuildPlatform = 'windows' | 'macos';
/** The build platform name of every release platform. */
export type BuildPlatform = TauriBuildPlatform | 'android';

/** A platform that ships a Tauri installer and updater package. */
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

/** The platform that ships the Android App; it has no Tauri facts. */
export interface AndroidReleasePlatform {
  readonly key: ReleasePlatformKey;
  readonly kind: 'android';
  readonly buildPlatform: 'android';
  readonly nodePlatform: null;
  readonly payloadHost: PayloadHost;
}

/** One row of RELEASE_PLATFORMS, discriminated by `kind`. */
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

// The platforms the Release Manifest (`latest.json`) waits for: it advances
// only when every one of them names the same release. Android ships on the
// game's APK cadence and stays outside, so it never holds back a desktop
// release (ADR 0012).
export const LOCKSTEP_PLATFORM_KEYS: readonly LockstepPlatformKey[] =
  Object.freeze([DOWNLOAD_PLATFORM_KEYS.windows, DOWNLOAD_PLATFORM_KEYS.mac]);

// The one lookup of a release platform by its build platform name.
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

// A platform that ships a Tauri installer; the Tauri build facts exist only
// on these.
export function tauriPlatform(buildPlatform: string): TauriReleasePlatform {
  const platform = releasePlatform(buildPlatform);
  if (platform.kind !== 'tauri')
    throw new Error(`${buildPlatform} does not ship a Tauri installer`);
  return platform;
}

// The Tauri platform a build platform, a Node `process.platform` name or a
// TAURI_ENV_PLATFORM value names; null when the host builds no installer.
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

// The Tauri installer build targets.
export function defaultTargetBuildPlatforms(): TauriBuildPlatform[] {
  return RELEASE_PLATFORMS.filter((platform) => platform.kind === 'tauri')
    .map((platform) => platform.buildPlatform)
    .sort();
}

// The part of a platform's Tauri config overlay the coherence check reads.
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

/**
 * The replay recorder plugin bundle inside a platform Payload, as the Payload
 * Inventory (`release/payload.json`) names it: the platform's only `native`
 * directory entry. bundle.sh signs and team-checks this path.
 */
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

function printLines(values: readonly (string | null)[]): void {
  process.stdout.write(`${values.map((value) => value ?? '').join('\n')}\n`);
}

export function cliMain(args: readonly string[]): number {
  const [verb, buildPlatform] = args;
  try {
    const platform = tauriPlatform(buildPlatform);
    switch (verb) {
      case 'rust-targets':
        printLines([platform.rustTarget]);
        break;
      case 'replay-recorder-bundle':
        printLines([replayRecorderBundlePath(platform.buildPlatform)]);
        break;
      case 'build-env':
        printLines([
          platform.tauriConfig,
          platform.resourceZip,
          platform.bundleTargets,
          platform.installerDir,
          platform.bundleCleanupDir,
          platform.releaseBinary,
          platform.rustTarget
        ]);
        break;
      default:
        throw new Error(`Unknown release-platforms verb: ${verb ?? ''}`);
    }
    return 0;
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    return 1;
  }
}

if (import.meta.main) {
  process.exitCode = cliMain(process.argv.slice(2));
}
