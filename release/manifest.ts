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
  AndroidAppDownload,
  AndroidGameRelease,
  DesktopGameRelease,
  InteropPackageDownload,
  ManagedPayloadDownload
} from './downloads.ts';
import type { ArtifactManifest, ArtifactRecord } from './artifact-manifest.ts';
import type { LoaderPin } from './snapshot-lock.ts';

export { RELEASE_MANIFEST_PATH, modManifestPath, platformManifestPath };

/** A release artifact on the release origin: its URL, size and sha256. */
export interface ReleaseArtifact {
  url: string;
  size: number;
  sha256: string;
}

/** A Tauri updater package and its signature text. */
export interface UpdaterArtifact extends ReleaseArtifact {
  signature: string;
}

/** A Loader Release as the Snapshot Lock pins it. */
export type AndroidLoader = Pick<LoaderPin, 'tag' | 'sha256'>;

/** The self-update facts of a desktop release (DESKTOP_SELF_UPDATE_FIELDS). */
export type DesktopSelfUpdateFacts = {
  managedPayload: ManagedPayloadDownload;
  selfUpdatable: boolean;
  historyDatabaseUserVersion: number;
  nonManagedPayloadDigest: string;
};

/** The release facts of an Android release (ANDROID_RELEASE_FIELDS). */
export type AndroidReleaseFacts = {
  app: AndroidAppDownload;
  managedPayload: ManagedPayloadDownload;
  interop: InteropPackageDownload;
  game: AndroidGameRelease;
  loader: AndroidLoader;
  selfUpdatable: boolean;
  historyDatabaseUserVersion: number;
  nonManagedPayloadDigest: string;
};

/**
 * The fragment `upload` writes beside a desktop release. The fields only an
 * Android fragment has are absent. `game` is the lock entry the build record
 * names; fragments uploaded before it existed lack it.
 */
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

/**
 * The fragment `upload --platform android` writes (kind `android-app`). The
 * fields only a desktop fragment has are absent.
 */
export interface AndroidPlatformFragment extends AndroidReleaseFacts {
  schemaVersion: 1;
  kind: 'android-app';
  version: ProductVersion;
  platform: ReleasePlatformKey;
  gitCommit: string;
  installer?: undefined;
  updater?: undefined;
  signatureFile?: undefined;
}

/** A platform fragment; its platform decides which of the two it is. */
export type PlatformFragment =
  | DesktopPlatformFragment
  | AndroidPlatformFragment;

/** The Mainland Mirror record stored beside an uploaded desktop release. */
export interface MirrorRecord {
  schemaVersion: 1;
  version: ProductVersion;
  platform: string;
  url: string;
  fileName: string;
  verified: boolean;
}

/** The Tauri updater entry of one platform. */
export interface UpdaterEntry {
  url: string;
  signature: string;
}

/** `downloads[platform]` of a desktop manifest: the installer and its facts. */
export interface DesktopDownload
  extends ReleaseArtifact, DesktopSelfUpdateFacts {
  mainlandUrl?: string;
  game?: DesktopGameRelease;
  apk?: undefined;
  app?: undefined;
}

/**
 * A Tauri static manifest: a desktop Platform Release Manifest, which holds
 * one platform's key, or the Release Manifest, which holds every Lockstep
 * Platform's.
 */
export interface DesktopPlatformManifest {
  version: ProductVersion;
  gitCommit: string;
  notes: string;
  pub_date: string;
  platforms: Record<string, UpdaterEntry>;
  downloads: Record<string, DesktopDownload>;
}

/** The Release Manifest (`latest.json`). */
export type ReleaseManifest = DesktopPlatformManifest;

/** `downloads[platform]` of an Android Platform Release Manifest. */
export interface AndroidDownloadRelease extends AndroidReleaseFacts {
  mainlandUrl?: string;
  apk?: undefined;
}

/** An Android Platform Release Manifest: it has no Tauri `platforms` key. */
export interface AndroidPlatformManifest {
  version: ProductVersion;
  gitCommit: string;
  notes: string;
  pub_date: string;
  platforms?: undefined;
  downloads: Record<string, AndroidDownloadRelease>;
}

/** A Platform Release Manifest; its platform decides which shape it has. */
export type PlatformManifest =
  | DesktopPlatformManifest
  | AndroidPlatformManifest;

/** The release an Android App APK carries (androidCarriedRelease). */
export interface AndroidCarriedRelease {
  version: ProductVersion;
  gitCommit: string;
  downloads: Record<string, Omit<AndroidReleaseFacts, 'app'>>;
}

// The artifact manifest `build` writes, as far as buildPlatformFragment
// reads it.
type ArtifactManifestFacts = Omit<
  ArtifactManifest,
  'schemaVersion' | 'payloadBuild' | 'builtAt'
>;

/** The inputs of androidNonManagedPayloadDigest. */
export interface AndroidNonManagedPayloadDigestOptions {
  loader: AndroidLoader;
  nativeLibs?: Record<string, { sha256: string }>;
  updaterSha256: string;
}

/** The inputs of buildMirrorRecord. */
export interface BuildMirrorRecordOptions {
  version: ProductVersion;
  platform: string;
  url: string;
  fileName: string;
  verified: boolean;
}

/** The inputs of buildPlatformFragment. */
export interface BuildPlatformFragmentOptions {
  manifest: ArtifactManifestFacts;
  baseUrl: string;
  selfUpdatable: boolean;
  /** The game the build compiled against: its build record's lock entry. */
  game: DesktopGameRelease;
}

/** The inputs of buildAndroidFragment: the facts of one Android build. */
export interface BuildAndroidFragmentOptions {
  version: ProductVersion;
  gitCommit: string;
  baseUrl: string;
  app: Pick<AndroidAppDownload, 'size' | 'sha256' | 'signerSha256'>;
  game: AndroidGameRelease;
  interop: Omit<InteropPackageDownload, 'url'>;
  managedPayload: Omit<ManagedPayloadDownload, 'url'>;
  loader: AndroidLoader;
  selfUpdatable: boolean;
  historyDatabaseUserVersion: number;
  nonManagedPayloadDigest: string;
}

/** The inputs of androidCarriedRelease: an Android build without its App. */
export type AndroidCarriedReleaseOptions = Omit<
  BuildAndroidFragmentOptions,
  'app' | 'selfUpdatable'
>;

/** The inputs of buildPlatformManifest. */
export interface BuildPlatformManifestOptions {
  version: ProductVersion;
  fragment: PlatformFragment;
  mirror?: MirrorRecord | null;
  existing?: PlatformManifest | null;
  now?: Date;
}

/** The inputs of mergeReleaseManifest. */
export interface MergeReleaseManifestOptions {
  platformManifests: readonly PlatformManifest[] | null | undefined;
  existingLatest: ReleaseManifest | null | undefined;
}

/** The inputs of buildLatestManifest. */
export interface BuildLatestManifestOptions {
  version: ProductVersion;
  fragments: readonly PlatformFragment[];
  mirrors?: Partial<Record<string, MirrorRecord>>;
  existingLatest?: ReleaseManifest | null;
  now?: Date;
}

// Artifact paths sit below the release origin's path (/installer).
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

// The Mainland Mirror address is an explicit operator input recorded next to
// the platform fragment; it is never derived from the version.
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

// The certificate of the BPP Android release key (ADR 0009, 2026-11-01 note):
// the one signer of every Android App APK and of every interop statement.
// upload and promote --platform android refuse an APK signed by any other
// certificate; the Android App embeds this certificate, and a test there pins
// it to the same digest.
export const ANDROID_RELEASE_SIGNER_SHA256 =
  '0d53bd4d2011c3e3181eb291c2154a0162f5dd9f4c4330b7bbc3728840851ce9';

// What the Android App carries besides the Mods (ADR 0012): the Loader
// Release and the native libraries of the Snapshot Lock's android-online
// entry, and the Updater plugin. The Interop Package downloads on its own and
// is not part of it. A fragment's `nonManagedPayloadDigest` is this digest of
// the App it names: release::build writes the same value into the App
// (assets/bpp-runtime.json) and reads it back, and an App that records
// another digest cannot take the release as a Managed Payload.
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

// The Android App's versionCode for a product version (ADR 0012): Android
// installs an App only over a lower versionCode, so it follows the product
// version. Gradle receives it as -Pbpp.versionCode.
export function androidAppVersionCode(version: ProductVersion): number {
  const [major, minor, patch] = assertProductVersion(version)
    .split('.')
    .map(Number);
  const code = major * 1_000_000 + minor * 1_000 + patch;
  // Google Play's ceiling; Android itself accepts up to 2^31 - 1.
  if (minor > 999 || patch > 999 || code < 1 || code > 2_100_000_000)
    throw new Error(
      `${version} has no Android versionCode (major * 1000000 + minor * 1000 + patch, minor and patch below 1000)`
    );
  return code;
}

const ANDROID_PLATFORM = DOWNLOAD_PLATFORM_KEYS.android;

// The Android App APK of one product version. The key is immutable once used.
export function androidAppKey(version: ProductVersion): string {
  assertProductVersion(version);
  return `${version}/${ANDROID_PLATFORM}/app/BazaarPlusPlus_${version}_${ANDROID_PLATFORM}.apk`;
}

// The Interop Package, addressed by the sha256 of the zip itself: releases
// of one Interop Snapshot share one object.
export function interopPackageKey(zipSha256: string): string {
  if (!SHA256.test(zipSha256 ?? ''))
    throw new Error(`Invalid Interop Package digest: ${zipSha256}`);
  return `android/interop/${zipSha256}.zip`;
}

const BASE64 =
  /^(?:[A-Za-z0-9+/]{4})+(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$/;

// The `interop` record of an Android fragment or Platform Release Manifest:
// the content-addressed zip, the Interop Snapshot digest it packs, and the
// base64 SHA256withRSA signature of its interop statement
// (release/android-interop.ts).
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

// The game a release is adapted to: the Snapshot Lock's android-online
// identity and its Game Code Hash.
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

// The game a desktop release was built against: a Snapshot Lock entry's game
// version and `Managed/` digest. Only the download page reads it.
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

// A Loader Release as the Snapshot Lock pins it. The tag names a directory
// under game-libs/ (loaderDirectory in release/interop-snapshot.ts), so it
// is a plain token.
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

// The Managed Payload zip of one product version on one platform.
export function managedPayloadKey(
  version: ProductVersion,
  platform: string
): string {
  return `${version}/${platform}/managed/BazaarPlusPlus_${version}_${platform}-managed.zip`;
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
      `${platform} mainland mirror was recorded for ${record.fileName}, but the release installer is ${fileName}`
    );
  return record;
}

// The self-update facts a desktop fragment and its Platform Release Manifest
// carry beside the installer, in this order (mod ADR-0014): the Managed
// Payload zip, the publisher's hint whether the previous release's installs
// can take it, and the two facts the mod compares against its running copy.
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

// The fragment `upload --platform android` writes beside the Android release
// (kind `android-app`, ADR 0012): the Android App APK, the Interop Package and
// the Managed Payload zip, the game they are adapted to, and the facts the
// mod's self-update compares against the copy it runs. The caller supplies
// the facts; the object keys and the App's versionCode follow from them.
export function buildAndroidFragment({
  version,
  gitCommit,
  baseUrl,
  app,
  game,
  interop,
  managedPayload,
  loader,
  selfUpdatable,
  historyDatabaseUserVersion,
  nonManagedPayloadDigest
}: BuildAndroidFragmentOptions): AndroidPlatformFragment {
  assertProductVersion(version);
  if (!GIT_COMMIT.test(gitCommit ?? ''))
    throw new Error('Android fragment requires an identified product build');
  const origin = assertReleaseOrigin(baseUrl);
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
    game: {
      gameVersion: game?.gameVersion,
      versionCode: game?.versionCode,
      il2cppSha256: game?.il2cppSha256,
      metadataSha256: game?.metadataSha256
    },
    interop: {
      // Without a digest the URL is absent; validation refuses the record
      // on its digest before it reads the URL.
      url: (SHA256.test(interop?.sha256 ?? '')
        ? `${origin}/${interopPackageKey(interop.sha256)}`
        : undefined) as string,
      size: interop?.size,
      sha256: interop?.sha256,
      snapshotSha256: interop?.snapshotSha256,
      signature: interop?.signature
    },
    managedPayload: {
      url: `${origin}/${managedPayloadKey(version, ANDROID_PLATFORM)}`,
      size: managedPayload?.size,
      sha256: managedPayload?.sha256,
      signature: managedPayload?.signature?.trim()
    },
    loader: { tag: loader?.tag, sha256: loader?.sha256 },
    selfUpdatable,
    historyDatabaseUserVersion,
    nonManagedPayloadDigest
  };
  return validatePlatformFragment(fragment, version, ANDROID_PLATFORM);
}

// The release an Android App APK carries (assets/bpp-release/release.json, ADR
// 0012): the shape of its Platform Release Manifest without `app`, because the
// APK cannot name its own digest. The App reads it when no manifest can be
// fetched, so a new install with a bad network still runs what it downloaded.
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
      // fromEntries keeps every field it is given but not their names.
      [ANDROID_PLATFORM]: Object.fromEntries(
        ANDROID_RELEASE_FIELDS.filter((field) => field !== 'app').map(
          (field) => [field, fragment[field]]
        )
      ) as Omit<AndroidReleaseFacts, 'app'>
    }
  };
}

// What an Android Platform Release Manifest publishes from its fragment, in
// this order: the three artifacts a new install downloads (App, then the
// Managed Payload and Interop Package it stages), the game they are adapted
// to and the Loader Release the App carries, then the self-update facts in
// the desktop order.
const ANDROID_RELEASE_FIELDS: readonly (keyof AndroidReleaseFacts)[] =
  Object.freeze([
    'app',
    'managedPayload',
    'interop',
    'game',
    'loader',
    'selfUpdatable',
    'historyDatabaseUserVersion',
    'nonManagedPayloadDigest'
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

// The release facts an Android fragment and its Platform Release Manifest
// share, at product version `version`.
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
  if (
    new URL(managedPayload.url).pathname !==
    `${RELEASE_BASE_PATH}/${managedPayloadKey(version, platform)}`
  )
    throw new Error(`Unexpected Managed Payload URL: ${managedPayload.url}`);
}

// ADR 0010's `kind: 'android'` fragments are refused by their kind, not by
// the fields they lack.
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
  validateAndroidRelease(fragment, version, platform);
  return fragment;
}

// Every remote object a fragment names; promotion checks each is in place.
export function fragmentArtifacts(
  fragment: PlatformFragment
): ReleaseArtifact[] {
  // A validated fragment has the shape its platform names.
  return releasePlatformByKey(fragment.platform).kind === 'android'
    ? [
        (fragment as AndroidPlatformFragment).app,
        (fragment as AndroidPlatformFragment).interop,
        fragment.managedPayload
      ]
    : [
        (fragment as DesktopPlatformFragment).installer,
        (fragment as DesktopPlatformFragment).updater,
        (fragment as DesktopPlatformFragment).signatureFile,
        fragment.managedPayload
      ];
}

// Returns the fragment it was given, as the shape its platform names.
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
  // A desktop platform's fragment is a desktop fragment.
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

// An Android Platform Release Manifest: the shared top-level shape without
// the Tauri `platforms` key; `downloads[platform]` holds the release facts.
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
  // Without a `platforms` key it is an Android App release.
  const { mainlandUrl, ...release } = (manifest as AndroidPlatformManifest)
    .downloads[platform];
  validateAndroidRelease(release, manifest.version, platform);
  if (mainlandUrl !== undefined) assertMirrorUrl(mainlandUrl);
  return manifest;
}

// Returns the manifest it was given, as the shape its platform names.
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
  // A desktop platform's manifest has the Tauri `platforms` key.
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

// One platform's promoted release: the Platform Release Manifest. It is a
// valid Tauri static manifest holding only that platform's key.
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
      // The Android platform's fragment is an Android App fragment.
      fragment: fragment as AndroidPlatformFragment,
      mirror,
      existing: reuse ? existing : null,
      now
    });
  // The fragment and manifest of a desktop platform are desktop ones.
  const desktop = fragment as DesktopPlatformFragment;
  // Once published, the manifest's own mirror address is authoritative; a
  // record can only confirm it, never replace it.
  const publishedMirror = reuse
    ? (existing as DesktopPlatformManifest).downloads[platform].mainlandUrl
    : undefined;
  const mainlandUrl = mirror
    ? validateMirrorRecord(
        mirror,
        version,
        platform,
        installerFileName(desktop.installer.url)
      ).url
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
        // fromEntries keeps every field it is given but not their names.
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

// Android has no Mainland Mirror yet (ADR 0012): promotion records none, and
// an address already published for this version would be kept. `existing` is
// the published manifest of this same version, or null.
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
  if (mirror) throw new Error(`${platform} does not record a mainland mirror`);
  // fromEntries keeps every field it is given but not their names.
  const release = Object.fromEntries(
    ANDROID_RELEASE_FIELDS.map((field) => [field, fragment[field]])
  ) as AndroidReleaseFacts;
  const publishedMirror = (existing as AndroidPlatformManifest | null)
    ?.downloads[platform].mainlandUrl;
  const manifest: AndroidPlatformManifest = {
    version,
    gitCommit: fragment.gitCommit,
    notes: existing ? existing.notes : `Release ${version}`,
    pub_date: existing ? existing.pub_date : now.toISOString(),
    downloads: {
      [platform]:
        publishedMirror === undefined
          ? release
          : { ...release, mainlandUrl: publishedMirror }
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

// The Release Manifest: the newest release every Lockstep Platform promoted
// at the same version and commit. Installers whose platform manifest fails to
// load read only this file, so it never runs ahead of any Lockstep Platform
// (ADR 0003). Android is not one (ADR 0012).
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
    // Only a desktop manifest has a Tauri `platforms` key to be found by.
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
  // Every Lockstep Platform has a manifest here, so one date is the newest.
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

// Lockstep convenience: every Lockstep Platform's fragment at one version and
// commit.
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
