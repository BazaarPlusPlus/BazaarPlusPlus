import crypto from 'node:crypto';
import path from 'node:path';
import { isDeepStrictEqual } from 'node:util';
import { assertProductVersion, compareProductVersions } from './product.ts';
import type { ProductVersion } from './product.ts';
import {
  LOCKSTEP_PLATFORM_KEYS,
  RELEASE_PLATFORMS,
  RELEASE_PLATFORM_KEYS,
  releasePlatformByKey
} from './release-platforms.ts';
import type { ReleasePlatformKey } from './release-platforms.ts';
import {
  DOWNLOAD_PLATFORM_KEYS,
  RELEASE_BASE_URL,
  RELEASE_MANIFEST_PATH,
  modManifestPath,
  platformManifestPath
} from './downloads.ts';
import type {
  AndroidAdaptation,
  AndroidAppDownload,
  AndroidGameRelease,
  DesktopGameRelease,
  InteropPackageDownload,
  ManagedPayloadDownload
} from './downloads.ts';
import type { ArtifactManifest, ArtifactRecord } from './artifact-manifest.ts';
import { ANDROID_SOURCES } from './snapshot-lock.ts';
import type { AndroidSource, LoaderPin } from './snapshot-lock.ts';

export { RELEASE_MANIFEST_PATH, modManifestPath, platformManifestPath };

export interface ReleaseArtifact {
  url: string;
  size: number;
  sha256: string;
}

export interface UpdaterArtifact extends ReleaseArtifact {
  signature: string;
}

export type AndroidLoader = Pick<LoaderPin, 'tag' | 'sha256'>;

export type DesktopSelfUpdateFacts = {
  managedPayload: ManagedPayloadDownload;
  selfUpdatable: boolean;
  historyDatabaseUserVersion: number;
  nonManagedPayloadDigest: string;
};

export type AndroidAdaptations = Record<AndroidSource, AndroidAdaptation>;

// Older Apps and mods read the publisher's adaptation from the top-level fields (ADR 0019).
export type AndroidReleaseFacts = {
  app: AndroidAppDownload;
  managedPayload: ManagedPayloadDownload;
  interop: InteropPackageDownload;
  game: AndroidGameRelease;
  loader: AndroidLoader;
  selfUpdatable: boolean;
  historyDatabaseUserVersion: number;
  nonManagedPayloadDigest: string;
  adaptations?: AndroidAdaptations;
};

/** Fragments uploaded before desktop game compatibility was tracked may omit `game`. */
export interface DesktopPlatformFragment extends DesktopSelfUpdateFacts {
  schemaVersion: 1;
  kind?: undefined;
  version: ProductVersion;
  platform: ReleasePlatformKey;
  gitCommit: string;
  installer: ReleaseArtifact;
  updater: UpdaterArtifact;
  signatureFile: ReleaseArtifact;
  app?: undefined;
  interop?: undefined;
  game?: DesktopGameRelease;
  loader?: undefined;
}

export interface AndroidPlatformFragment extends AndroidReleaseFacts {
  schemaVersion: 1;
  kind: 'android-app';
  version: ProductVersion;
  platform: ReleasePlatformKey;
  gitCommit: string;
  adaptations: AndroidAdaptations;
  installer?: undefined;
  updater?: undefined;
  signatureFile?: undefined;
}

export type PlatformFragment =
  | DesktopPlatformFragment
  | AndroidPlatformFragment;

export interface MirrorRecord {
  schemaVersion: 1;
  version: ProductVersion;
  platform: string;
  url: string;
  fileName: string;
  verified: boolean;
}

export interface UpdaterEntry {
  url: string;
  signature: string;
}

export interface DesktopDownload
  extends ReleaseArtifact, DesktopSelfUpdateFacts {
  mainlandUrl?: string;
  game?: DesktopGameRelease;
  apk?: undefined;
  app?: undefined;
}

export interface DesktopPlatformManifest {
  version: ProductVersion;
  gitCommit: string;
  notes: string;
  pub_date: string;
  platforms: Record<string, UpdaterEntry>;
  downloads: Record<string, DesktopDownload>;
}

export type ReleaseManifest = DesktopPlatformManifest;

export interface AndroidDownloadRelease extends AndroidReleaseFacts {
  mainlandUrl?: string;
  apk?: undefined;
}

export interface AndroidPlatformManifest {
  version: ProductVersion;
  gitCommit: string;
  notes: string;
  pub_date: string;
  platforms?: undefined;
  downloads: Record<string, AndroidDownloadRelease>;
}

export type PlatformManifest =
  | DesktopPlatformManifest
  | AndroidPlatformManifest;

export interface AndroidCarriedRelease {
  version: ProductVersion;
  gitCommit: string;
  downloads: Record<string, Omit<AndroidReleaseFacts, 'app'>>;
}

type ArtifactManifestFacts = Omit<
  ArtifactManifest,
  'schemaVersion' | 'payloadBuild' | 'builtAt'
>;

export interface AndroidNonManagedPayloadDigestOptions {
  loader: AndroidLoader;
  nativeLibs?: Record<string, { sha256: string }>;
  updaterSha256: string;
}

export interface BuildMirrorRecordOptions {
  version: ProductVersion;
  platform: string;
  url: string;
  fileName: string;
  verified: boolean;
}

export interface BuildPlatformFragmentOptions {
  manifest: ArtifactManifestFacts;
  baseUrl: string;
  selfUpdatable: boolean;
  game: DesktopGameRelease;
}

export interface AndroidAdaptationFacts {
  game: AndroidGameRelease;
  interop: Omit<InteropPackageDownload, 'url'>;
  managedPayload: Omit<ManagedPayloadDownload, 'url'>;
}

export interface BuildAndroidFragmentOptions {
  version: ProductVersion;
  gitCommit: string;
  baseUrl: string;
  app: Pick<AndroidAppDownload, 'size' | 'sha256' | 'signerSha256'>;
  adaptations: Record<AndroidSource, AndroidAdaptationFacts>;
  loader: AndroidLoader;
  selfUpdatable: boolean;
  historyDatabaseUserVersion: number;
  nonManagedPayloadDigest: string;
}

export type AndroidCarriedReleaseOptions = Omit<
  BuildAndroidFragmentOptions,
  'app' | 'selfUpdatable'
>;

export interface BuildPlatformManifestOptions {
  version: ProductVersion;
  fragment: PlatformFragment;
  mirror?: MirrorRecord | null;
  existing?: PlatformManifest | null;
  now?: Date;
}

export interface MergeReleaseManifestOptions {
  platformManifests: readonly PlatformManifest[] | null | undefined;
  existingLatest: ReleaseManifest | null | undefined;
}

export interface BuildLatestManifestOptions {
  version: ProductVersion;
  fragments: readonly PlatformFragment[];
  mirrors?: Partial<Record<string, MirrorRecord>>;
  existingLatest?: ReleaseManifest | null;
  now?: Date;
}

const RELEASE_BASE_PATH = new URL(RELEASE_BASE_URL).pathname.replace(/\/$/, '');

const SHA256 = /^[a-f0-9]{64}$/;
const GIT_COMMIT = /^[a-f0-9]{40,64}$/;

function assertRecord(
  record: ReleaseArtifact | undefined,
  description: string
): asserts record is ReleaseArtifact {
  if (
    !record ||
    !Number.isSafeInteger(record.size) ||
    record.size <= 0 ||
    !/^[a-f0-9]{64}$/.test(record.sha256 ?? '')
  )
    throw new Error(`Invalid ${description} release artifact`);
  const url = new URL(record.url);
  if (
    url.protocol !== 'https:' ||
    url.username ||
    url.password ||
    url.hash ||
    url.search
  )
    throw new Error(`Unsafe ${description} release URL`);
}

export function installerFileName(installerUrl: string): string {
  const fileName = decodeURIComponent(
    path.posix.basename(new URL(installerUrl).pathname)
  );
  if (!fileName)
    throw new Error(`Installer URL has no file name: ${installerUrl}`);
  return fileName;
}

// The download the Mainland Mirror serves: the desktop installer or the Android App APK.
export function mirrorArtifact(fragment: PlatformFragment): ReleaseArtifact {
  return fragment.kind === 'android-app' ? fragment.app : fragment.installer;
}

export function mirrorFileName(fragment: PlatformFragment): string {
  return installerFileName(mirrorArtifact(fragment).url);
}

export function assertMirrorUrl(value: string): string {
  let url: URL;
  try {
    url = new URL(value);
  } catch {
    throw new Error(`Invalid mainland mirror URL: ${JSON.stringify(value)}`);
  }
  if (url.protocol !== 'https:' || url.username || url.password)
    throw new Error(`Unsafe mainland mirror URL: ${value}`);
  return value;
}

// R2 object keys stored beside each uploaded release; never change them.
export function mirrorRecordKey(
  version: ProductVersion,
  platform: string
): string {
  return `${version}/${platform}/mirror/mainland.json`;
}

export function fragmentKey(version: ProductVersion, platform: string): string {
  return `${version}/${platform}/updater/platform-manifest.json`;
}

// The Android App embeds this certificate and uses it to verify Interop Packages (ADR 0009).
export const ANDROID_RELEASE_SIGNER_SHA256 =
  '0d53bd4d2011c3e3181eb291c2154a0162f5dd9f4c4330b7bbc3728840851ce9';

// The Android App selects an adaptation by the installed game's signer (ADR 0019).
export const ANDROID_GAME_SIGNERS: Readonly<Record<AndroidSource, string>> =
  Object.freeze({
    publisher:
      'f8f508cf8b590e715a2f5bdba78098f03daf988cf828dd61fcbee8aa9b21632a',
    play: 'f093884efa76e15b6375faa0c1ef195f4f5c411719e568e043fd82103c399b72'
  });

// The fragment and the App's assets/bpp-runtime.json must carry the same digest for self-update.
export function androidNonManagedPayloadDigest({
  loader,
  nativeLibs = {},
  updaterSha256
}: AndroidNonManagedPayloadDigestOptions): string {
  if (!SHA256.test(updaterSha256 ?? ''))
    throw new Error('An Android App runtime needs its Updater plugin digest');
  const libs = Object.keys(nativeLibs)
    .sort()
    .map((name) => ({ name, sha256: nativeLibs[name].sha256 }));
  return crypto
    .createHash('sha256')
    .update(
      JSON.stringify({
        loader: { tag: loader.tag, sha256: loader.sha256 },
        nativeLibs: libs,
        updaterSha256
      })
    )
    .digest('hex');
}

// Android uses versionCode to enforce upgrade ordering.
export function androidAppVersionCode(version: ProductVersion): number {
  const [major, minor, patch] = assertProductVersion(version)
    .split('.')
    .map(Number);
  const code = major * 1_000_000 + minor * 1_000 + patch;
  // Use Google Play's versionCode ceiling.
  if (minor > 999 || patch > 999 || code < 1 || code > 2_100_000_000)
    throw new Error(
      `${version} has no Android versionCode (major * 1000000 + minor * 1000 + patch, minor and patch below 1000)`
    );
  return code;
}

const ANDROID_PLATFORM = DOWNLOAD_PLATFORM_KEYS.android;

// Published APK keys are immutable.
export function androidAppKey(version: ProductVersion): string {
  assertProductVersion(version);
  return `${version}/${ANDROID_PLATFORM}/app/BazaarPlusPlus_${version}_${ANDROID_PLATFORM}.apk`;
}

// Content addressing lets releases share the same Interop Package.
export function interopPackageKey(zipSha256: string): string {
  if (!SHA256.test(zipSha256 ?? ''))
    throw new Error(`Invalid Interop Package digest: ${zipSha256}`);
  return `android/interop/${zipSha256}.zip`;
}

const BASE64 =
  /^(?:[A-Za-z0-9+/]{4})+(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$/;

function assertInteropRecord(
  record: InteropPackageDownload,
  description: string
): InteropPackageDownload {
  assertRecord(record, `${description} Interop Package`);
  if (
    !SHA256.test(record.snapshotSha256 ?? '') ||
    typeof record.signature !== 'string' ||
    !BASE64.test(record.signature)
  )
    throw new Error(`Invalid ${description} Interop Package`);
  if (
    new URL(record.url).pathname !==
    `${RELEASE_BASE_PATH}/${interopPackageKey(record.sha256)}`
  )
    throw new Error(`Unexpected Interop Package URL: ${record.url}`);
  return record;
}

function assertAndroidGame(
  game: AndroidGameRelease,
  description: string
): AndroidGameRelease {
  if (
    typeof game?.gameVersion !== 'string' ||
    !game.gameVersion ||
    !positiveInteger(game.versionCode) ||
    !SHA256.test(game.il2cppSha256 ?? '') ||
    !SHA256.test(game.metadataSha256 ?? '')
  )
    throw new Error(`Invalid ${description} game`);
  return game;
}

function assertDesktopGame(
  game: DesktopGameRelease,
  description: string
): DesktopGameRelease {
  if (
    typeof game?.gameVersion !== 'string' ||
    !game.gameVersion ||
    !SHA256.test(game.managedSha256 ?? '')
  )
    throw new Error(`Invalid ${description} game`);
  return game;
}

// The tag must be a plain token because loaderDirectory uses it in a path.
function assertAndroidLoader(
  loader: AndroidLoader,
  description: string
): AndroidLoader {
  if (
    typeof loader?.tag !== 'string' ||
    !/^[A-Za-z0-9][A-Za-z0-9._-]*$/.test(loader.tag) ||
    !SHA256.test(loader.sha256 ?? '')
  )
    throw new Error(`Invalid ${description} loader`);
  return loader;
}

export function managedPayloadKey(
  version: ProductVersion,
  platform: string
): string {
  return `${version}/${platform}/managed/BazaarPlusPlus_${version}_${platform}-managed.zip`;
}

// App 6.2.5 checks only the version's managed/ prefix. Newer Apps require the exact key.
export function androidManagedPayloadKey(
  version: ProductVersion,
  source: AndroidSource
): string {
  return `${version}/${ANDROID_PLATFORM}/managed/${source}/BazaarPlusPlus_${version}_${ANDROID_PLATFORM}-${source}-managed.zip`;
}

function assertReleaseOrigin(baseUrl: string): string {
  const base = new URL(baseUrl);
  if (
    base.protocol !== 'https:' ||
    base.username ||
    base.password ||
    base.search ||
    base.hash
  )
    throw new Error('Unsafe release origin');
  return baseUrl.replace(/\/$/, '');
}

export function buildMirrorRecord({
  version,
  platform,
  url,
  fileName,
  verified
}: BuildMirrorRecordOptions): MirrorRecord {
  return validateMirrorRecord(
    {
      schemaVersion: 1,
      version: assertProductVersion(version),
      platform,
      url,
      fileName,
      verified: Boolean(verified)
    },
    version,
    platform,
    fileName
  );
}

export function validateMirrorRecord(
  record: MirrorRecord,
  version: ProductVersion,
  platform: string,
  fileName: string
): MirrorRecord {
  if (
    record?.schemaVersion !== 1 ||
    record.version !== version ||
    record.platform !== platform ||
    typeof record.verified !== 'boolean'
  )
    throw new Error(
      `Invalid ${platform} mainland mirror record for ${version}`
    );
  assertMirrorUrl(record.url);
  if (record.fileName !== fileName)
    throw new Error(
      `${platform} mainland mirror was recorded for ${record.fileName}, but the release ships ${fileName}`
    );
  return record;
}

const DESKTOP_SELF_UPDATE_FIELDS: readonly (keyof DesktopSelfUpdateFacts)[] =
  Object.freeze([
    'managedPayload',
    'selfUpdatable',
    'historyDatabaseUserVersion',
    'nonManagedPayloadDigest'
  ]);

function assertSelfUpdateFacts(
  release: DesktopSelfUpdateFacts,
  version: ProductVersion,
  platform: string
): void {
  if (
    typeof release.selfUpdatable !== 'boolean' ||
    !positiveInteger(release.historyDatabaseUserVersion) ||
    !SHA256.test(release.nonManagedPayloadDigest ?? '')
  )
    throw new Error(`Invalid ${platform} self-update facts for ${version}`);
  assertManagedPayload(release.managedPayload, version, platform);
  if (
    new URL(release.managedPayload.url).pathname !==
    `${RELEASE_BASE_PATH}/${managedPayloadKey(version, platform)}`
  )
    throw new Error(
      `Unexpected Managed Payload URL: ${release.managedPayload.url}`
    );
}

export function buildPlatformFragment({
  manifest,
  baseUrl,
  selfUpdatable,
  game
}: BuildPlatformFragmentOptions): DesktopPlatformFragment {
  const version = assertProductVersion(manifest.appVersion);
  const definition = RELEASE_PLATFORMS.find(
    (platform) =>
      platform.key === manifest.releasePlatformKey &&
      platform.buildPlatform === manifest.buildPlatform
  );
  if (
    !definition ||
    definition.kind !== 'tauri' ||
    manifest.dirty ||
    !GIT_COMMIT.test(manifest.gitCommit ?? '')
  )
    throw new Error(
      'Platform fragment requires a clean, identified product build'
    );
  const origin = assertReleaseOrigin(baseUrl);
  const record = (
    artifact: ArtifactRecord,
    category: string
  ): ReleaseArtifact => ({
    url: `${origin}/${version}/${definition.key}/${category}/${encodeURIComponent(path.basename(artifact.path))}`,
    size: artifact.size,
    sha256: artifact.sha256
  });
  const fragment: DesktopPlatformFragment = {
    schemaVersion: 1,
    version,
    platform: definition.key,
    gitCommit: manifest.gitCommit,
    installer: record(manifest.installer, 'installer'),
    updater: {
      ...record(manifest.updater, 'updater'),
      signature: manifest.signature.content.trim()
    },
    signatureFile: record(manifest.signature, 'updater'),
    game: {
      gameVersion: game?.gameVersion,
      managedSha256: game?.managedSha256
    },
    managedPayload: {
      url: `${origin}/${managedPayloadKey(version, definition.key)}`,
      size: manifest.managedPayload?.size,
      sha256: manifest.managedPayload?.sha256,
      signature: manifest.managedPayload?.signature?.trim()
    },
    selfUpdatable,
    historyDatabaseUserVersion: manifest.historyDatabaseUserVersion,
    nonManagedPayloadDigest: manifest.nonManagedPayloadDigest
  };
  validatePlatformFragment(fragment, version, definition.key);
  return fragment;
}

export function buildAndroidFragment({
  version,
  gitCommit,
  baseUrl,
  app,
  adaptations: facts,
  loader,
  selfUpdatable,
  historyDatabaseUserVersion,
  nonManagedPayloadDigest
}: BuildAndroidFragmentOptions): AndroidPlatformFragment {
  assertProductVersion(version);
  if (!GIT_COMMIT.test(gitCommit ?? ''))
    throw new Error('Android fragment requires an identified product build');
  const origin = assertReleaseOrigin(baseUrl);
  const adaptation = (source: AndroidSource): AndroidAdaptation => {
    const { game, interop, managedPayload } = facts?.[source] ?? {};
    const interopSha256 = interop?.sha256 ?? '';
    return {
      signerSha256: ANDROID_GAME_SIGNERS[source],
      game: {
        gameVersion: game?.gameVersion,
        versionCode: game?.versionCode,
        il2cppSha256: game?.il2cppSha256,
        metadataSha256: game?.metadataSha256
      },
      interop: {
        url: (SHA256.test(interopSha256)
          ? `${origin}/${interopPackageKey(interopSha256)}`
          : undefined) as string,
        size: interop?.size,
        sha256: interop?.sha256,
        snapshotSha256: interop?.snapshotSha256,
        signature: interop?.signature
      },
      managedPayload: {
        url: `${origin}/${androidManagedPayloadKey(version, source)}`,
        size: managedPayload?.size,
        sha256: managedPayload?.sha256,
        signature: managedPayload?.signature?.trim()
      }
    } as AndroidAdaptation;
  };
  const adaptations = Object.fromEntries(
    ANDROID_SOURCES.map((source) => [source, adaptation(source)])
  ) as AndroidAdaptations;
  const { game, interop, managedPayload } = adaptations.publisher;
  const fragment: AndroidPlatformFragment = {
    schemaVersion: 1,
    kind: 'android-app',
    version,
    platform: ANDROID_PLATFORM,
    gitCommit,
    app: {
      url: `${origin}/${androidAppKey(version)}`,
      size: app?.size,
      sha256: app?.sha256,
      versionCode: androidAppVersionCode(version),
      signerSha256: app?.signerSha256
    },
    game: { ...game },
    interop: { ...interop },
    managedPayload: { ...managedPayload },
    loader: { tag: loader?.tag, sha256: loader?.sha256 },
    selfUpdatable,
    historyDatabaseUserVersion,
    nonManagedPayloadDigest,
    adaptations
  };
  return validatePlatformFragment(fragment, version, ANDROID_PLATFORM);
}

// The App uses its carried manifest when offline. The APK cannot include its own digest.
export function androidCarriedRelease({
  baseUrl,
  ...release
}: AndroidCarriedReleaseOptions): AndroidCarriedRelease {
  const fragment = buildAndroidFragment({
    ...release,
    baseUrl,
    app: {
      size: 1,
      sha256: '0'.repeat(64),
      signerSha256: ANDROID_RELEASE_SIGNER_SHA256
    },
    selfUpdatable: false
  });
  return {
    version: fragment.version,
    gitCommit: fragment.gitCommit,
    downloads: {
      [ANDROID_PLATFORM]: Object.fromEntries(
        ANDROID_RELEASE_FIELDS.filter((field) => field !== 'app').map(
          (field) => [field, fragment[field]]
        )
      ) as Omit<AndroidReleaseFacts, 'app'>
    }
  };
}

const ANDROID_RELEASE_FIELDS: readonly (keyof AndroidReleaseFacts)[] =
  Object.freeze([
    'app',
    'managedPayload',
    'interop',
    'game',
    'loader',
    'selfUpdatable',
    'historyDatabaseUserVersion',
    'nonManagedPayloadDigest',
    'adaptations'
  ]);

function positiveInteger(value: number): boolean {
  return Number.isSafeInteger(value) && value > 0;
}

function assertManagedPayload(
  record: ManagedPayloadDownload,
  version: ProductVersion,
  platform: string
): void {
  assertRecord(record, `${platform} Managed Payload`);
  if (typeof record.signature !== 'string' || !record.signature.trim())
    throw new Error(`Missing Managed Payload signature: ${platform}`);
  if (
    !new URL(record.url).pathname.startsWith(
      `${RELEASE_BASE_PATH}/${version}/${platform}/managed/`
    )
  )
    throw new Error(`Artifact URL is outside its release: ${record.url}`);
}

function validateAndroidRelease(
  release: AndroidReleaseFacts,
  version: ProductVersion,
  platform: string
): void {
  const { app, managedPayload } = release;
  if (
    typeof release.selfUpdatable !== 'boolean' ||
    !positiveInteger(release.historyDatabaseUserVersion) ||
    !SHA256.test(release.nonManagedPayloadDigest ?? '')
  )
    throw new Error(`Invalid ${platform} release facts for ${version}`);
  assertAndroidGame(release.game, `${platform} ${version}`);
  assertAndroidLoader(release.loader, `${platform} ${version}`);
  assertRecord(app, `${platform} Android App`);
  if (
    app.versionCode !== androidAppVersionCode(version) ||
    !SHA256.test(app.signerSha256 ?? '')
  )
    throw new Error(`Invalid ${platform} Android App for ${version}`);
  if (
    new URL(app.url).pathname !==
    `${RELEASE_BASE_PATH}/${androidAppKey(version)}`
  )
    throw new Error(`Unexpected Android App URL: ${app.url}`);
  assertInteropRecord(release.interop, `${platform} ${version}`);
  assertManagedPayload(managedPayload, version, platform);
  const { adaptations } = release;
  // A manifest promoted before ADR 0019 has one Managed Payload per version.
  const managedKey = adaptations
    ? androidManagedPayloadKey(version, 'publisher')
    : managedPayloadKey(version, platform);
  if (
    new URL(managedPayload.url).pathname !==
    `${RELEASE_BASE_PATH}/${managedKey}`
  )
    throw new Error(`Unexpected Managed Payload URL: ${managedPayload.url}`);
  if (adaptations === undefined) return;
  if (!isDeepStrictEqual(Object.keys(adaptations), ANDROID_SOURCES))
    throw new Error(
      `${platform} ${version} adaptations must be exactly ${ANDROID_SOURCES.join(', ')}`
    );
  for (const source of ANDROID_SOURCES) {
    const adaptation = adaptations[source];
    const description = `${platform} ${version} ${source}`;
    if (adaptation?.signerSha256 !== ANDROID_GAME_SIGNERS[source])
      throw new Error(`Invalid ${description} game signer`);
    assertAndroidGame(adaptation.game, description);
    assertInteropRecord(adaptation.interop, description);
    assertManagedPayload(adaptation.managedPayload, version, platform);
    if (
      new URL(adaptation.managedPayload.url).pathname !==
      `${RELEASE_BASE_PATH}/${androidManagedPayloadKey(version, source)}`
    )
      throw new Error(
        `Unexpected Managed Payload URL: ${adaptation.managedPayload.url}`
      );
  }
  const { game, interop } = adaptations.publisher;
  if (
    !isDeepStrictEqual(release.game, game) ||
    !isDeepStrictEqual(release.interop, interop) ||
    !isDeepStrictEqual(managedPayload, adaptations.publisher.managedPayload)
  )
    throw new Error(
      `${platform} ${version} game, interop and managedPayload must be the publisher adaptation's`
    );
}

function validateAndroidFragment<F extends PlatformFragment>(
  fragment: F,
  version: ProductVersion,
  platform: string
): F {
  if (fragment?.kind !== 'android-app')
    throw new Error(
      `${platform} platform fragment for ${version} is of kind ${JSON.stringify(fragment?.kind)}, not android-app`
    );
  if (
    fragment.schemaVersion !== 1 ||
    fragment.version !== version ||
    fragment.platform !== platform ||
    !GIT_COMMIT.test(fragment.gitCommit ?? '')
  )
    throw new Error(`Invalid ${platform} platform fragment for ${version}`);
  if (fragment.adaptations === undefined)
    throw new Error(
      `${platform} platform fragment for ${version} has no adaptations`
    );
  validateAndroidRelease(fragment, version, platform);
  return fragment;
}

export function fragmentArtifacts(
  fragment: PlatformFragment
): ReleaseArtifact[] {
  if (releasePlatformByKey(fragment.platform).kind === 'android') {
    const android = fragment as AndroidPlatformFragment;
    const artifacts = [
      android.app,
      ...Object.values(android.adaptations).flatMap(
        ({ interop, managedPayload }) => [interop, managedPayload]
      )
    ];
    return artifacts.filter(
      (artifact, index) =>
        artifacts.findIndex(({ url }) => url === artifact.url) === index
    );
  }
  return [
    (fragment as DesktopPlatformFragment).installer,
    (fragment as DesktopPlatformFragment).updater,
    (fragment as DesktopPlatformFragment).signatureFile,
    fragment.managedPayload
  ];
}

export function validatePlatformFragment<F extends PlatformFragment>(
  fragment: F,
  version: ProductVersion,
  platform: string
): F {
  if (releasePlatformByKey(platform).kind === 'android')
    return validateAndroidFragment(fragment, version, platform);
  if (
    fragment?.schemaVersion !== 1 ||
    fragment.version !== version ||
    fragment.platform !== platform ||
    !GIT_COMMIT.test(fragment.gitCommit ?? '')
  )
    throw new Error(`Invalid ${platform} platform fragment for ${version}`);
  assertSelfUpdateFacts(fragment, version, platform);
  const { game } = fragment as DesktopPlatformFragment;
  if (game !== undefined) assertDesktopGame(game, `${platform} ${version}`);
  assertRecord(fragment.installer, 'installer');
  assertRecord(fragment.updater, 'updater');
  assertRecord(fragment.signatureFile, 'signature');
  if (
    typeof fragment.updater.signature !== 'string' ||
    !fragment.updater.signature.trim()
  )
    throw new Error(`Missing updater signature: ${platform}`);
  for (const [record, category] of [
    [fragment.installer, 'installer'],
    [fragment.updater, 'updater'],
    [fragment.signatureFile, 'updater']
  ] as const) {
    const url = new URL(record.url);
    if (
      !url.pathname.startsWith(
        `${RELEASE_BASE_PATH}/${version}/${platform}/${category}/`
      )
    )
      throw new Error(`Artifact URL is outside its release: ${record.url}`);
  }
  return fragment;
}

function assertSafeHttps(value: string, description: string): string {
  const url = new URL(value);
  if (url.protocol !== 'https:' || url.username || url.password)
    throw new Error(`Unsafe ${description} URL`);
  return value;
}

function validateAndroidPlatformManifest<M extends PlatformManifest>(
  manifest: M,
  platform: string
): M {
  if (
    !manifest ||
    typeof manifest !== 'object' ||
    !GIT_COMMIT.test(manifest.gitCommit ?? '') ||
    typeof manifest.notes !== 'string' ||
    typeof manifest.pub_date !== 'string' ||
    manifest.platforms !== undefined ||
    !isDeepStrictEqual(Object.keys(manifest.downloads ?? {}), [platform])
  )
    throw new Error(`Invalid ${platform} platform manifest`);
  assertProductVersion(manifest.version);
  const { mainlandUrl, ...release } = (manifest as AndroidPlatformManifest)
    .downloads[platform];
  validateAndroidRelease(release, manifest.version, platform);
  if (mainlandUrl !== undefined) assertMirrorUrl(mainlandUrl);
  return manifest;
}

export function validatePlatformManifest<M extends PlatformManifest>(
  manifest: M,
  platform: string
): M {
  if (releasePlatformByKey(platform).kind === 'android')
    return validateAndroidPlatformManifest(manifest, platform);
  if (
    !manifest ||
    typeof manifest !== 'object' ||
    !GIT_COMMIT.test(manifest.gitCommit ?? '') ||
    typeof manifest.notes !== 'string' ||
    typeof manifest.pub_date !== 'string' ||
    !isDeepStrictEqual(Object.keys(manifest.platforms ?? {}), [platform]) ||
    !isDeepStrictEqual(Object.keys(manifest.downloads ?? {}), [platform])
  )
    throw new Error(`Invalid ${platform} platform manifest`);
  assertProductVersion(manifest.version);
  const desktop = manifest as DesktopPlatformManifest;
  const updater = desktop.platforms[platform];
  if (
    typeof updater?.signature !== 'string' ||
    !updater.signature.trim() ||
    typeof updater.url !== 'string'
  )
    throw new Error(`Invalid ${platform} platform manifest updater entry`);
  assertSafeHttps(updater.url, `${platform} updater`);
  const { mainlandUrl, game, ...release } = desktop.downloads[platform];
  const installer: ReleaseArtifact & Partial<DesktopSelfUpdateFacts> = {
    ...release
  };
  for (const field of DESKTOP_SELF_UPDATE_FIELDS) delete installer[field];
  assertRecord(installer, `${platform} installer`);
  assertSelfUpdateFacts(release, manifest.version, platform);
  if (mainlandUrl !== undefined) assertMirrorUrl(mainlandUrl);
  if (game !== undefined)
    assertDesktopGame(game, `${platform} ${manifest.version}`);
  return manifest;
}

// Tauri accepts a static manifest with just the current platform's key.
export function buildPlatformManifest({
  version,
  fragment,
  mirror = null,
  existing = null,
  now = new Date()
}: BuildPlatformManifestOptions): PlatformManifest {
  assertProductVersion(version);
  const platform = fragment?.platform;
  if (!RELEASE_PLATFORM_KEYS.includes(platform))
    throw new Error(`Unknown release platform fragment: ${platform}`);
  validatePlatformFragment(fragment, version, platform);
  if (existing) {
    validatePlatformManifest(existing, platform);
    if (compareProductVersions(version, existing.version) < 0)
      throw new Error(
        `Refusing ${platform} release rollback from ${existing.version} to ${version}`
      );
  }
  const reuse = existing?.version === version;
  if (releasePlatformByKey(platform).kind === 'android')
    return buildAndroidPlatformManifest({
      version,
      fragment: fragment as AndroidPlatformFragment,
      mirror,
      existing: reuse ? existing : null,
      now
    });
  const desktop = fragment as DesktopPlatformFragment;
  // Once published, the manifest's own mirror address is authoritative; a
  // record can only confirm it, never replace it.
  const publishedMirror = reuse
    ? (existing as DesktopPlatformManifest).downloads[platform].mainlandUrl
    : undefined;
  const mainlandUrl = mirror
    ? validateMirrorRecord(mirror, version, platform, mirrorFileName(desktop))
        .url
    : publishedMirror;
  const manifest: DesktopPlatformManifest = {
    version,
    gitCommit: fragment.gitCommit,
    notes: reuse ? existing.notes : `Release ${version}`,
    pub_date: reuse ? existing.pub_date : now.toISOString(),
    platforms: {
      [platform]: {
        url: desktop.updater.url,
        signature: desktop.updater.signature
      }
    },
    downloads: {
      [platform]: {
        ...desktop.installer,
        ...(mainlandUrl === undefined ? {} : { mainlandUrl }),
        ...(desktop.game === undefined ? {} : { game: desktop.game }),
        ...(Object.fromEntries(
          DESKTOP_SELF_UPDATE_FIELDS.map((field) => [field, desktop[field]])
        ) as DesktopSelfUpdateFacts)
      }
    }
  };
  if (reuse) {
    for (const field of ['gitCommit', 'platforms', 'downloads'] as const) {
      if (!isDeepStrictEqual(existing[field], manifest[field]))
        throw new Error(
          `Published ${version} ${platform} ${field} differs; publish a new product version`
        );
    }
  }
  return manifest;
}

// The APK is built before a Mainland Mirror exists. The first promoted manifest fixes that URL.
function buildAndroidPlatformManifest({
  version,
  fragment,
  mirror,
  existing,
  now
}: {
  version: ProductVersion;
  fragment: AndroidPlatformFragment;
  mirror: MirrorRecord | null;
  existing: PlatformManifest | null;
  now: Date;
}): AndroidPlatformManifest {
  const platform = fragment.platform;
  const release = Object.fromEntries(
    ANDROID_RELEASE_FIELDS.map((field) => [field, fragment[field]])
  ) as AndroidReleaseFacts;
  const publishedMirror = (existing as AndroidPlatformManifest | null)
    ?.downloads[platform].mainlandUrl;
  const mainlandUrl = mirror
    ? validateMirrorRecord(mirror, version, platform, mirrorFileName(fragment))
        .url
    : publishedMirror;
  const manifest: AndroidPlatformManifest = {
    version,
    gitCommit: fragment.gitCommit,
    notes: existing ? existing.notes : `Release ${version}`,
    pub_date: existing ? existing.pub_date : now.toISOString(),
    downloads: {
      [platform]:
        mainlandUrl === undefined ? release : { ...release, mainlandUrl }
    }
  };
  if (existing) {
    for (const field of ['gitCommit', 'downloads'] as const) {
      if (!isDeepStrictEqual(existing[field], manifest[field]))
        throw new Error(
          `Published ${version} ${platform} ${field} differs; publish a new product version`
        );
    }
  }
  return manifest;
}

// Installers fall back to latest.json, so it cannot advance ahead of any Lockstep Platform (ADR 0003).
export function mergeReleaseManifest({
  platformManifests,
  existingLatest
}: MergeReleaseManifestOptions): ReleaseManifest {
  const byPlatform = new Map(
    (platformManifests ?? []).map((manifest) => [
      Object.keys(manifest?.platforms ?? {})[0],
      manifest
    ])
  );
  const ordered = LOCKSTEP_PLATFORM_KEYS.map((key) => {
    const manifest = byPlatform.get(key);
    if (!manifest)
      throw new Error(
        `Release requires every lockstep platform; missing ${key}`
      );
    return validatePlatformManifest(manifest as DesktopPlatformManifest, key);
  });
  if (new Set(ordered.map((manifest) => manifest.version)).size !== 1)
    throw new Error('Release platform versions differ');
  if (new Set(ordered.map((manifest) => manifest.gitCommit)).size !== 1)
    throw new Error('Release platform commits differ');
  const version = ordered[0].version;
  if (
    existingLatest &&
    compareProductVersions(version, existingLatest.version) < 0
  )
    throw new Error(
      `Refusing release rollback from ${existingLatest.version} to ${version}`
    );
  const reuse = existingLatest?.version === version;
  const newestPubDate = ordered
    .map((manifest) => manifest.pub_date)
    .sort()
    .at(-1);
  if (newestPubDate === undefined)
    throw new Error('Release requires every lockstep platform');
  const latest: ReleaseManifest = {
    version,
    gitCommit: ordered[0].gitCommit,
    notes: reuse ? existingLatest.notes : `Release ${version}`,
    pub_date: reuse ? existingLatest.pub_date : newestPubDate,
    platforms: {},
    downloads: {}
  };
  for (const manifest of ordered) {
    const [key] = Object.keys(manifest.platforms);
    latest.platforms[key] = manifest.platforms[key];
    latest.downloads[key] = manifest.downloads[key];
  }
  if (reuse) {
    for (const field of ['gitCommit', 'platforms', 'downloads'] as const) {
      if (!isDeepStrictEqual(existingLatest[field], latest[field]))
        throw new Error(
          `Published ${version} ${field} differs; publish a new product version`
        );
    }
  }
  return latest;
}

export function buildLatestManifest({
  version,
  fragments,
  mirrors = {},
  existingLatest,
  now = new Date()
}: BuildLatestManifestOptions): ReleaseManifest {
  assertProductVersion(version);
  if (
    !Array.isArray(fragments) ||
    fragments.length !== LOCKSTEP_PLATFORM_KEYS.length
  )
    throw new Error(`Release ${version} requires every lockstep platform`);
  const platformManifests = fragments.map((fragment) =>
    buildPlatformManifest({
      version,
      fragment,
      mirror: mirrors[fragment?.platform] ?? null,
      now
    })
  );
  return mergeReleaseManifest({ platformManifests, existingLatest });
}
