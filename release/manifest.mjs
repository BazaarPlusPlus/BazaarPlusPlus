import crypto from 'node:crypto';
import path from 'node:path';
import { isDeepStrictEqual } from 'node:util';
import { assertProductVersion, compareProductVersions } from './product.mjs';
import {
  LOCKSTEP_PLATFORM_KEYS,
  RELEASE_PLATFORMS,
  RELEASE_PLATFORM_KEYS,
  releasePlatformByKey
} from './release-platforms.mjs';
import {
  RELEASE_BASE_URL,
  RELEASE_MANIFEST_PATH,
  platformManifestPath
} from './downloads.ts';

export { RELEASE_MANIFEST_PATH, platformManifestPath };

// Artifact paths sit below the release origin's path (/installer).
const RELEASE_BASE_PATH = new URL(RELEASE_BASE_URL).pathname.replace(/\/$/, '');

const SHA256 = /^[a-f0-9]{64}$/;
const GIT_COMMIT = /^[a-f0-9]{40,64}$/;

function assertRecord(record, description) {
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

export function installerFileName(installerUrl) {
  const fileName = decodeURIComponent(
    path.posix.basename(new URL(installerUrl).pathname)
  );
  if (!fileName)
    throw new Error(`Installer URL has no file name: ${installerUrl}`);
  return fileName;
}

// The Mainland Mirror address is an explicit operator input recorded next to
// the platform fragment; it is never derived from the version.
export function assertMirrorUrl(value) {
  let url;
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
export function mirrorRecordKey(version, platform) {
  return `${version}/${platform}/mirror/mainland.json`;
}

export function fragmentKey(version, platform) {
  return `${version}/${platform}/updater/platform-manifest.json`;
}

// What an installed Android Release APK carries besides the managed files and
// the game: the Loader Release, the Interop Snapshot and the native libraries
// the injection adds, as the Snapshot Lock's android-online entry pins them,
// and the Updater plugin baked into its deployment. A fragment's
// `nonManagedPayloadDigest` is this digest of the APK it names; with the game
// version it is that APK's identity (ADR 0010), and a running copy that
// records another digest cannot take the release as a Managed Payload.
export function androidNonManagedPayloadDigest({
  loader,
  interopSha256,
  nativeLibs = {},
  updaterSha256
}) {
  if (!SHA256.test(updaterSha256 ?? ''))
    throw new Error('An Android APK identity needs its Updater plugin digest');
  const libs = Object.keys(nativeLibs)
    .sort()
    .map((name) => ({ name, sha256: nativeLibs[name].sha256 }));
  return crypto
    .createHash('sha256')
    .update(
      JSON.stringify({
        loader: { tag: loader.tag, sha256: loader.sha256 },
        interopSha256,
        nativeLibs: libs,
        updaterSha256
      })
    )
    .digest('hex');
}

// The Android Release APK is named by what it is built from (game build,
// loader tag, the first eight hex digits of its non-managed payload digest),
// never by the product version: one APK serves every product version until
// the game, the loader, the interop or a native library changes (ADR 0010).
// The key is immutable once used.
export function androidApkKey({
  gameVersion,
  loaderTag,
  nonManagedPayloadDigest
}) {
  const build = /^(\d+(?:\.\d+)+)-/.exec(gameVersion ?? '')?.[1];
  if (
    !build ||
    !/^[A-Za-z0-9._-]+$/.test(loaderTag ?? '') ||
    !SHA256.test(nonManagedPayloadDigest ?? '')
  )
    throw new Error(
      'An Android APK is identified by its game version, loader tag and non-managed payload digest'
    );
  return `android/apk/BazaarPlusPlus_${build}_lemon-${loaderTag}_${nonManagedPayloadDigest.slice(0, 8)}.apk`;
}

// The Managed Payload zip of one product version on one platform.
export function managedPayloadKey(version, platform) {
  return `${version}/${platform}/managed/BazaarPlusPlus_${version}_${platform}-managed.zip`;
}

function assertReleaseOrigin(baseUrl) {
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
}) {
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

export function validateMirrorRecord(record, version, platform, fileName) {
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
const DESKTOP_SELF_UPDATE_FIELDS = Object.freeze([
  'managedPayload',
  'selfUpdatable',
  'historyDatabaseUserVersion',
  'nonManagedPayloadDigest'
]);

function assertSelfUpdateFacts(release, version, platform) {
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

export function buildPlatformFragment({ manifest, baseUrl, selfUpdatable }) {
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
  const record = (artifact, category) => ({
    url: `${origin}/${version}/${definition.key}/${category}/${encodeURIComponent(path.basename(artifact.path))}`,
    size: artifact.size,
    sha256: artifact.sha256
  });
  const fragment = {
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

// The fragment `upload --platform android` writes beside the Android release:
// the APK, the Managed Payload zip, and the facts the mod's self-update
// compares against the copy it runs. The caller supplies the facts; the object
// keys follow from them.
export function buildAndroidFragment({
  version,
  gitCommit,
  baseUrl,
  apk,
  managedPayload,
  selfUpdatable,
  historyDatabaseUserVersion,
  nonManagedPayloadDigest,
  game,
  loader,
  interopSha256
}) {
  assertProductVersion(version);
  if (!GIT_COMMIT.test(gitCommit ?? ''))
    throw new Error('Android fragment requires an identified product build');
  const origin = assertReleaseOrigin(baseUrl);
  const platform = RELEASE_PLATFORMS.find(
    (definition) => definition.kind === 'android'
  ).key;
  const fragment = {
    schemaVersion: 2,
    kind: 'android',
    version,
    platform,
    gitCommit,
    apk: {
      url: `${origin}/${androidApkKey({
        gameVersion: game?.gameVersion,
        loaderTag: loader?.tag,
        nonManagedPayloadDigest
      })}`,
      size: apk?.size,
      sha256: apk?.sha256,
      versionCode: apk?.versionCode,
      signerSha256: apk?.signerSha256,
      builtForProductVersion: apk?.builtForProductVersion
    },
    managedPayload: {
      url: `${origin}/${managedPayloadKey(version, platform)}`,
      size: managedPayload?.size,
      sha256: managedPayload?.sha256,
      signature: managedPayload?.signature?.trim()
    },
    selfUpdatable,
    historyDatabaseUserVersion,
    nonManagedPayloadDigest,
    game: {
      gameVersion: game?.gameVersion,
      versionCode: game?.versionCode,
      officialApkSha256: game?.officialApkSha256
    },
    loader: { tag: loader?.tag, sha256: loader?.sha256 },
    interopSha256
  };
  return validatePlatformFragment(fragment, version, platform);
}

// What an Android Platform Release Manifest publishes from its fragment, in
// this order.
const ANDROID_RELEASE_FIELDS = Object.freeze([
  'apk',
  'managedPayload',
  'selfUpdatable',
  'historyDatabaseUserVersion',
  'nonManagedPayloadDigest',
  'game',
  'loader',
  'interopSha256'
]);

function positiveInteger(value) {
  return Number.isSafeInteger(value) && value > 0;
}

function assertManagedPayload(record, version, platform) {
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
function validateAndroidRelease(release, version, platform) {
  const { apk, managedPayload, game, loader } = release;
  if (
    typeof game?.gameVersion !== 'string' ||
    !positiveInteger(game.versionCode) ||
    !SHA256.test(game.officialApkSha256 ?? '') ||
    typeof loader?.tag !== 'string' ||
    !SHA256.test(loader.sha256 ?? '') ||
    !SHA256.test(release.interopSha256 ?? '') ||
    typeof release.selfUpdatable !== 'boolean' ||
    !positiveInteger(release.historyDatabaseUserVersion) ||
    !SHA256.test(release.nonManagedPayloadDigest ?? '')
  )
    throw new Error(`Invalid ${platform} release facts for ${version}`);
  assertRecord(apk, `${platform} APK`);
  if (
    !positiveInteger(apk.versionCode) ||
    !SHA256.test(apk.signerSha256 ?? '') ||
    compareProductVersions(
      assertProductVersion(apk.builtForProductVersion),
      version
    ) > 0
  )
    throw new Error(`Invalid ${platform} APK for ${version}`);
  const apkKey = androidApkKey({
    gameVersion: game.gameVersion,
    loaderTag: loader.tag,
    nonManagedPayloadDigest: release.nonManagedPayloadDigest
  });
  if (new URL(apk.url).pathname !== `${RELEASE_BASE_PATH}/${apkKey}`)
    throw new Error(
      `${platform} APK URL does not name its game, loader and non-managed payload digest: ${apk.url}`
    );
  assertManagedPayload(managedPayload, version, platform);
  if (
    new URL(managedPayload.url).pathname !==
    `${RELEASE_BASE_PATH}/${managedPayloadKey(version, platform)}`
  )
    throw new Error(`Unexpected Managed Payload URL: ${managedPayload.url}`);
}

function validateAndroidFragment(fragment, version, platform) {
  if (
    fragment?.schemaVersion !== 2 ||
    fragment.kind !== 'android' ||
    fragment.version !== version ||
    fragment.platform !== platform ||
    !GIT_COMMIT.test(fragment.gitCommit ?? '')
  )
    throw new Error(`Invalid ${platform} platform fragment for ${version}`);
  validateAndroidRelease(fragment, version, platform);
  return fragment;
}

// Every remote object a fragment names; promotion checks each is in place.
export function fragmentArtifacts(fragment) {
  return releasePlatformByKey(fragment.platform).kind === 'android'
    ? [fragment.apk, fragment.managedPayload]
    : [
        fragment.installer,
        fragment.updater,
        fragment.signatureFile,
        fragment.managedPayload
      ];
}

export function validatePlatformFragment(fragment, version, platform) {
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
  ]) {
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

function assertSafeHttps(value, description) {
  const url = new URL(value);
  if (url.protocol !== 'https:' || url.username || url.password)
    throw new Error(`Unsafe ${description} URL`);
  return value;
}

// An Android Platform Release Manifest: the shared top-level shape without
// the Tauri `platforms` key; `downloads[platform]` holds the release facts.
function validateAndroidPlatformManifest(manifest, platform) {
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
  const { mainlandUrl, ...release } = manifest.downloads[platform];
  validateAndroidRelease(release, manifest.version, platform);
  if (mainlandUrl !== undefined) assertMirrorUrl(mainlandUrl);
  return manifest;
}

export function validatePlatformManifest(manifest, platform) {
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
  const updater = manifest.platforms[platform];
  if (
    typeof updater?.signature !== 'string' ||
    !updater.signature.trim() ||
    typeof updater.url !== 'string'
  )
    throw new Error(`Invalid ${platform} platform manifest updater entry`);
  assertSafeHttps(updater.url, `${platform} updater`);
  const { mainlandUrl, ...release } = manifest.downloads[platform];
  const installer = { ...release };
  for (const field of DESKTOP_SELF_UPDATE_FIELDS) delete installer[field];
  assertRecord(installer, `${platform} installer`);
  assertSelfUpdateFacts(release, manifest.version, platform);
  if (mainlandUrl !== undefined) assertMirrorUrl(mainlandUrl);
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
}) {
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
      fragment,
      mirror,
      existing: reuse ? existing : null,
      now
    });
  // Once published, the manifest's own mirror address is authoritative; a
  // record can only confirm it, never replace it.
  const publishedMirror = reuse
    ? existing.downloads[platform].mainlandUrl
    : undefined;
  const mainlandUrl = mirror
    ? validateMirrorRecord(
        mirror,
        version,
        platform,
        installerFileName(fragment.installer.url)
      ).url
    : publishedMirror;
  const manifest = {
    version,
    gitCommit: fragment.gitCommit,
    notes: reuse ? existing.notes : `Release ${version}`,
    pub_date: reuse ? existing.pub_date : now.toISOString(),
    platforms: {
      [platform]: {
        url: fragment.updater.url,
        signature: fragment.updater.signature
      }
    },
    downloads: {
      [platform]: {
        ...fragment.installer,
        ...(mainlandUrl === undefined ? {} : { mainlandUrl }),
        ...Object.fromEntries(
          DESKTOP_SELF_UPDATE_FIELDS.map((field) => [field, fragment[field]])
        )
      }
    }
  };
  if (reuse) {
    for (const field of ['gitCommit', 'platforms', 'downloads']) {
      if (!isDeepStrictEqual(existing[field], manifest[field]))
        throw new Error(
          `Published ${version} ${platform} ${field} differs; publish a new product version`
        );
    }
  }
  return manifest;
}

// Android has no Mainland Mirror yet (ADR 0010): promotion records none, and
// an address already published for this version would be kept. `existing` is
// the published manifest of this same version, or null.
function buildAndroidPlatformManifest({
  version,
  fragment,
  mirror,
  existing,
  now
}) {
  const platform = fragment.platform;
  if (mirror) throw new Error(`${platform} does not record a mainland mirror`);
  const release = Object.fromEntries(
    ANDROID_RELEASE_FIELDS.map((field) => [field, fragment[field]])
  );
  const publishedMirror = existing?.downloads[platform].mainlandUrl;
  const manifest = {
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
    for (const field of ['gitCommit', 'downloads']) {
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
// (ADR 0003). Android is not one (ADR 0010).
export function mergeReleaseManifest({ platformManifests, existingLatest }) {
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
    return validatePlatformManifest(manifest, key);
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
  const latest = {
    version,
    gitCommit: ordered[0].gitCommit,
    notes: reuse ? existingLatest.notes : `Release ${version}`,
    pub_date: reuse
      ? existingLatest.pub_date
      : ordered
          .map((manifest) => manifest.pub_date)
          .sort()
          .at(-1),
    platforms: {},
    downloads: {}
  };
  for (const manifest of ordered) {
    const [key] = Object.keys(manifest.platforms);
    latest.platforms[key] = manifest.platforms[key];
    latest.downloads[key] = manifest.downloads[key];
  }
  if (reuse) {
    for (const field of ['gitCommit', 'platforms', 'downloads']) {
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
}) {
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
