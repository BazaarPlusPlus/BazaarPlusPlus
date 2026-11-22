import fs from 'node:fs';
import crypto from 'node:crypto';
import path from 'node:path';
import { isDeepStrictEqual } from 'node:util';
import { compareProductVersions, readProductVersion } from './product.ts';
import { putImmutable, putImmutableFile, putReplaceable } from './r2-store.ts';
import {
  ANDROID_RELEASE_SIGNER_SHA256,
  RELEASE_MANIFEST_PATH,
  buildAndroidFragment,
  buildLatestManifest,
  buildMirrorRecord,
  buildPlatformFragment,
  buildPlatformManifest,
  fragmentArtifacts,
  fragmentKey,
  androidManagedPayloadKey,
  managedPayloadKey,
  mergeReleaseManifest,
  mirrorArtifact,
  mirrorFileName,
  mirrorRecordKey,
  modManifestPath,
  platformManifestPath,
  validateMirrorRecord,
  validatePlatformFragment,
  validatePlatformManifest
} from './manifest.ts';
import type {
  AndroidPlatformFragment,
  AndroidPlatformManifest,
  DesktopPlatformFragment,
  DesktopPlatformManifest,
  MirrorRecord,
  PlatformFragment,
  PlatformManifest,
  ReleaseArtifact,
  ReleaseManifest
} from './manifest.ts';
import {
  assertMainlandMirrors,
  checkMainlandMirror,
  mainlandMirrorUrl,
  pushMainlandMirror
} from './mirror.ts';
import type { RunRsync } from './mirror.ts';
import type { ProductVersion } from './product.ts';
import type { FetchImpl, ObjectStore, StoredObject } from './r2-store.ts';
import {
  LOCKSTEP_PLATFORM_KEYS,
  RELEASE_PLATFORM_KEYS,
  releasePlatform,
  releasePlatformByKey
} from './release-platforms.ts';
import type {
  ReleasePlatformKey,
  TauriBuildPlatform
} from './release-platforms.ts';
import {
  artifactManifestPath,
  gitStateForRoot,
  validateArtifactManifest
} from './artifact-manifest.ts';
import type { GitState } from './artifact-manifest.ts';
import { readAndroidArtifacts } from './android-release.ts';
import { ANDROID_SOURCES } from './snapshot-lock.ts';

export interface UploadPlatformOptions {
  workspaceRoot: string;
  platform: TauriBuildPlatform;
  baseUrl: string;
  store: ObjectStore;
}

export interface UploadAndroidPlatformOptions {
  workspaceRoot: string;
  baseUrl: string;
  store: ObjectStore;
  gitState?: (rootDir: string) => GitState;
  releaseSignerSha256?: string;
  log?: (line: string) => void;
}

export interface RecordMainlandMirrorOptions {
  version: ProductVersion;
  platform: string;
  store: ObjectStore;
  sshKey: string;
  fetchImpl?: FetchImpl;
  runRsync?: RunRsync;
  allowUnverified?: boolean;
  log?: (line: string) => void;
}

export interface PromotePlatformOptions {
  version: ProductVersion;
  platform: string;
  baseUrl: string;
  store: ObjectStore;
  withoutMainlandMirror?: boolean;
  log?: (line: string) => void;
  releaseSignerSha256?: string;
  now?: Date;
}

export interface PromotePlatformResult {
  platform: PlatformManifest;
  latest: ReleaseManifest | null;
  advanced: boolean;
}

export interface PromoteReleaseOptions {
  version: ProductVersion;
  baseUrl: string;
  store: ObjectStore;
  withoutMainlandMirror?: boolean;
  log?: (line: string) => void;
  now?: Date;
}

type MirrorRecords = Partial<Record<string, MirrorRecord>>;

interface ReadMirrorsOptions {
  withoutMainlandMirror: boolean;
  confirming: (platform: string) => boolean;
  log: (line: string) => void;
}

interface WritePlatformManifestOptions {
  version: ProductVersion;
  fragment: PlatformFragment;
  mirror: MirrorRecord | null;
  now: Date;
}

const jsonBytes = (value: unknown): Buffer =>
  Buffer.from(`${JSON.stringify(value, null, 2)}\n`);
function readJsonObject(
  object: StoredObject | null,
  description: string
): unknown {
  if (!object)
    throw new Error(`Missing ${description}; run upload for it first`);
  try {
    return JSON.parse(object.bytes.toString('utf8'));
  } catch {
    throw new Error(`Invalid JSON in ${description}`);
  }
}

function readOptionalJson(
  object: StoredObject | null,
  description: string
): unknown {
  return object ? readJsonObject(object, description) : null;
}

async function readFragment<F extends PlatformFragment = PlatformFragment>(
  store: ObjectStore,
  version: ProductVersion,
  platform: string
): Promise<F> {
  return validatePlatformFragment(
    readJsonObject(
      await store.get(fragmentKey(version, platform)),
      `${platform} platform fragment`
    ) as F,
    version,
    platform
  );
}

async function readPlatformManifest<
  M extends PlatformManifest = PlatformManifest
>(store: ObjectStore, platform: string): Promise<M | null> {
  const object = await store.get(platformManifestPath(platform));
  return object
    ? validatePlatformManifest(
        readJsonObject(object, `${platform} platform manifest`) as M,
        platform
      )
    : null;
}

async function readLatest(store: ObjectStore): Promise<ReleaseManifest | null> {
  return readOptionalJson(
    await store.get(RELEASE_MANIFEST_PATH),
    RELEASE_MANIFEST_PATH
  ) as ReleaseManifest | null;
}

// A product version names one commit on every platform, Android included,
// across time as well as across hosts: a platform may ship a version later,
// but only that commit.
async function assertSameCommitAcrossPlatforms(
  store: ObjectStore,
  version: ProductVersion,
  fragment: PlatformFragment
): Promise<void> {
  for (const platform of RELEASE_PLATFORM_KEYS) {
    if (platform === fragment.platform) continue;
    const other = readOptionalJson(
      await store.get(fragmentKey(version, platform)),
      `${platform} platform fragment`
    ) as PlatformFragment | null;
    if (other && other.gitCommit !== fragment.gitCommit)
      throw new Error(
        `${version} was uploaded from commit ${other.gitCommit} on ${platform}; build that commit or publish a new product version`
      );
  }
}

async function assertArtifactsStored(
  store: ObjectStore,
  fragment: PlatformFragment,
  baseUrl: string
): Promise<void> {
  for (const record of fragmentArtifacts(fragment)) {
    const key = objectKey(record.url, baseUrl);
    const stored = await store.head(key);
    if (
      !stored ||
      stored.size !== record.size ||
      stored.sha256 !== record.sha256
    )
      throw new Error(`Release artifact is missing or differs: ${key}`);
  }
}

// The published mirror is authoritative on retry, even if the original mirror record has changed.
async function readMirrors(
  store: ObjectStore,
  version: ProductVersion,
  fragments: readonly PlatformFragment[],
  { withoutMainlandMirror, confirming, log }: ReadMirrorsOptions
): Promise<MirrorRecords> {
  const mirrors: MirrorRecords = {};
  for (const fragment of fragments) {
    if (confirming(fragment.platform)) continue;
    const record = readOptionalJson(
      await store.get(mirrorRecordKey(version, fragment.platform)),
      `${fragment.platform} mainland mirror record`
    ) as MirrorRecord | null;
    if (record) {
      mirrors[fragment.platform] = validateMirrorRecord(
        record,
        version,
        fragment.platform,
        mirrorFileName(fragment)
      );
    } else if (withoutMainlandMirror) {
      log(
        `WARNING: publishing ${fragment.platform} without a mainland mirror; it cannot be added to ${version} later`
      );
    } else {
      throw new Error(
        `Missing ${fragment.platform} mainland mirror for ${version}; run mirror for it or pass --without-mainland-mirror`
      );
    }
  }
  return mirrors;
}

// Read back after a failed PUT to distinguish a lost response from a failed write.
async function compareAndSwapJson<T>(
  store: ObjectStore,
  key: string,
  build: (existing: T | null) => T | null,
  maxAttempts = 4
): Promise<T> {
  for (let attempt = 0; attempt < maxAttempts; attempt++) {
    const current = await store.get(key);
    const existing = readOptionalJson(current, key) as T | null;
    const next = build(existing);
    if (next === null) {
      if (existing === null)
        throw new Error(`Writing ${key} kept an object that does not exist`);
      return existing;
    }
    const bytes = jsonBytes(next);
    try {
      if (
        await store.put(key, bytes, {
          ifMatch: current?.etag,
          ifNoneMatch: !current,
          contentType: 'application/json'
        })
      )
        return next;
    } catch (error) {
      const observed = await store.get(key);
      if (observed?.bytes.equals(bytes)) return next;
      throw error;
    }
  }
  throw new Error(
    `Writing ${key} conflicted repeatedly; retry after the other publisher finishes`
  );
}

function objectKey(url: string, baseUrl: string): string {
  const base = `${baseUrl.replace(/\/$/, '')}/`;
  if (!url.startsWith(base))
    throw new Error('Release artifact has an unexpected origin');
  return decodeURIComponent(url.slice(base.length));
}

// The mod compares against its running copy. Repeat the published hint on re-upload.
async function selfUpdatableFromPromoted(
  store: ObjectStore,
  platform: string,
  version: ProductVersion,
  facts: { historyDatabaseUserVersion: number; nonManagedPayloadDigest: string }
): Promise<boolean> {
  const manifest = await readPlatformManifest(store, platform);
  const promoted = manifest?.downloads[platform];
  if (manifest?.version === version)
    return manifest.downloads[platform].selfUpdatable;
  return (
    promoted?.historyDatabaseUserVersion === facts.historyDatabaseUserVersion &&
    promoted?.nonManagedPayloadDigest === facts.nonManagedPayloadDigest
  );
}

export async function uploadPlatform({
  workspaceRoot,
  platform,
  baseUrl,
  store
}: UploadPlatformOptions): Promise<DesktopPlatformFragment> {
  const rootDir = path.join(workspaceRoot, 'bazaarplusplus-installer');
  const version = readProductVersion(workspaceRoot);
  const validated = validateArtifactManifest({
    rootDir,
    manifestPath: artifactManifestPath(rootDir, platform),
    platform,
    version
  });
  const fragment = buildPlatformFragment({
    manifest: validated.manifest,
    baseUrl,
    game: {
      gameVersion: validated.lockEntry.gameVersion,
      managedSha256: validated.lockEntry.sha256
    },
    selfUpdatable: await selfUpdatableFromPromoted(
      store,
      validated.manifest.releasePlatformKey,
      version,
      validated.manifest
    )
  });
  // Freeze and verify every byte before the first remote write. A concurrent
  // local build must never poison an immutable key halfway through an upload.
  const snapshots = new Map<string, Buffer>();
  const uploads = (
    [
      [validated.installer, fragment.installer],
      [validated.updater, fragment.updater],
      [validated.signature, fragment.signatureFile],
      [validated.managedPayload, fragment.managedPayload]
    ] as const
  ).map(([file, record]) => {
    if (!snapshots.has(file)) snapshots.set(file, fs.readFileSync(file));
    const bytes = snapshots.get(file);
    if (bytes === undefined) throw new Error(`Artifact was not read: ${file}`);
    if (
      bytes.length !== record.size ||
      crypto.createHash('sha256').update(bytes).digest('hex') !== record.sha256
    )
      throw new Error(`Artifact changed before upload: ${file}`);
    return { key: objectKey(record.url, baseUrl), bytes };
  });
  await assertSameCommitAcrossPlatforms(store, version, fragment);
  for (const { key, bytes } of uploads) {
    await putImmutable(store, key, bytes);
  }
  await putImmutable(
    store,
    `${managedPayloadKey(version, fragment.platform)}.sig`,
    Buffer.from(`${fragment.managedPayload.signature}\n`),
    'text/plain'
  );
  await putImmutable(
    store,
    fragmentKey(version, fragment.platform),
    jsonBytes(fragment),
    'application/json'
  );
  return fragment;
}

// Android upgrades require the signer to match the already-installed App (ADR 0009).
async function assertAndroidSigner(
  store: ObjectStore,
  platform: string,
  signerSha256: string,
  releaseSignerSha256: string
): Promise<void> {
  if (signerSha256 !== releaseSignerSha256)
    throw new Error(
      `The Android App APK is signed by ${signerSha256}, not the release key ${releaseSignerSha256} (docs/adr/0009-android-release-key.md)`
    );
  const manifest = await readPlatformManifest<AndroidPlatformManifest>(
    store,
    platform
  );
  if (!manifest) return;
  const promoted = manifest.downloads[platform].app.signerSha256;
  if (promoted !== signerSha256)
    throw new Error(
      `The Android App APK is signed by ${signerSha256}, not the release key ${promoted} the promoted Android App carries; installed users could not update over it (docs/adr/0009-android-release-key.md)`
    );
}

export async function uploadAndroidPlatform({
  workspaceRoot,
  baseUrl,
  store,
  gitState = gitStateForRoot,
  releaseSignerSha256 = ANDROID_RELEASE_SIGNER_SHA256,
  log = () => {}
}: UploadAndroidPlatformOptions): Promise<AndroidPlatformFragment> {
  const version = readProductVersion(workspaceRoot);
  const { manifest, files } = await readAndroidArtifacts(workspaceRoot);
  const checkout = gitState(
    path.join(workspaceRoot, 'bazaarplusplus-installer')
  );
  if (manifest.gitCommit !== checkout.commit)
    throw new Error(
      `Android build commit mismatch: built ${manifest.gitCommit}, current ${checkout.commit}`
    );
  if (checkout.dirty)
    throw new Error('Current checkout is dirty; refusing upload');
  const { nonManagedPayloadDigest, historyDatabaseUserVersion } = manifest;
  const fragment = buildAndroidFragment({
    version,
    gitCommit: manifest.gitCommit,
    baseUrl,
    app: manifest.app,
    adaptations: manifest.adaptations,
    loader: manifest.loader,
    selfUpdatable: await selfUpdatableFromPromoted(
      store,
      releasePlatform('android').key,
      version,
      { historyDatabaseUserVersion, nonManagedPayloadDigest }
    ),
    historyDatabaseUserVersion,
    nonManagedPayloadDigest
  });
  const named: [string, string][] = [
    [fragment.app.url, manifest.app.key],
    ...ANDROID_SOURCES.map((source): [string, string] => [
      fragment.adaptations[source].interop.url,
      manifest.adaptations[source].interop.key
    ])
  ];
  for (const [url, key] of named)
    if (objectKey(url, baseUrl) !== key)
      throw new Error(`Android build names ${key}, not ${url}`);
  if (fragment.app.versionCode !== manifest.app.versionCode)
    throw new Error(
      `The Android App was built as versionCode ${manifest.app.versionCode}, not ${fragment.app.versionCode}`
    );
  await assertSameCommitAcrossPlatforms(store, version, fragment);
  await assertAndroidSigner(
    store,
    fragment.platform,
    fragment.app.signerSha256,
    releaseSignerSha256
  );
  const interops = ANDROID_SOURCES.map(
    (source) => fragment.adaptations[source].interop
  );
  const uploads: [ReleaseArtifact, string, string][] = [
    [fragment.app, files.app, 'application/vnd.android.package-archive'],
    ...ANDROID_SOURCES.flatMap(
      (source): [ReleaseArtifact, string, string][] => [
        [
          fragment.adaptations[source].interop,
          files.adaptations[source].interop,
          'application/zip'
        ],
        [
          fragment.adaptations[source].managedPayload,
          files.adaptations[source].managedPayload,
          'application/zip'
        ]
      ]
    )
  ];
  for (const [record, file, contentType] of uploads) {
    const key = objectKey(record.url, baseUrl);
    // HEAD lets another adaptation reuse a stored Interop Package without sending it again.
    const stored = interops.includes(record as (typeof interops)[number])
      ? await store.head(key)
      : null;
    const outcome =
      stored?.size === record.size && stored.sha256 === record.sha256
        ? 'present'
        : await putImmutableFile(store, key, file, {
            sha256: record.sha256,
            size: record.size,
            contentType
          });
    log(
      outcome === 'present'
        ? `Already stored: ${key}`
        : `Uploaded ${key} (${record.size} bytes)`
    );
  }
  for (const source of ANDROID_SOURCES)
    await putImmutable(
      store,
      `${androidManagedPayloadKey(version, source)}.sig`,
      Buffer.from(`${fragment.adaptations[source].managedPayload.signature}\n`),
      'text/plain'
    );
  await putImmutable(
    store,
    fragmentKey(version, fragment.platform),
    jsonBytes(fragment),
    'application/json'
  );
  return fragment;
}

// Android's mirror remains recordable after latest.json advances because Android releases independently.
async function assertMirrorRecordable(
  store: ObjectStore,
  version: ProductVersion,
  platform: string
): Promise<void> {
  const key = releasePlatformByKey(platform).key;
  const sources: [string, { version: ProductVersion } | null][] = [
    [platformManifestPath(key), await readPlatformManifest(store, key)]
  ];
  if ((LOCKSTEP_PLATFORM_KEYS as readonly ReleasePlatformKey[]).includes(key))
    sources.push([RELEASE_MANIFEST_PATH, await readLatest(store)]);
  for (const [source, published] of sources) {
    if (published && compareProductVersions(version, published.version) <= 0)
      throw new Error(
        `${source} has already published ${published.version}; a mainland mirror for ${key} ${version} can no longer be recorded, publish a new product version`
      );
  }
}

// Push, prove the served bytes, then record: the address is derived from the
// mirror host, the version and the file name, never typed by an operator.
export async function recordMainlandMirror({
  version,
  platform,
  store,
  sshKey,
  fetchImpl = fetch,
  runRsync,
  allowUnverified = false,
  log = () => {}
}: RecordMainlandMirrorOptions): Promise<MirrorRecord> {
  const key = releasePlatform(platform).key;
  const fragment = await readFragment(store, version, key);
  await assertMirrorRecordable(store, version, key);
  await pushMainlandMirror({ version, fragment, sshKey, fetchImpl, runRsync });
  const fileName = mirrorFileName(fragment);
  const url = mainlandMirrorUrl(version, fileName);
  const result = await checkMainlandMirror({
    platform: key,
    url,
    fileName,
    artifact: mirrorArtifact(fragment),
    fetchImpl
  });
  log(`Mainland mirror ${key}: ${result.outcome} (${result.detail})`);
  assertMainlandMirrors([result], { allowUnverified, log });
  const record = buildMirrorRecord({
    version,
    platform: key,
    url,
    fileName,
    verified: result.outcome === 'verified'
  });
  await writeMirrorRecord(store, record);
  return record;
}

async function writeMirrorRecord(
  store: ObjectStore,
  record: MirrorRecord
): Promise<void> {
  const { version, platform: key, url } = record;
  // The push and check took minutes; a promotion may have published the
  // previous record meanwhile. The published address wins, so re-check before and after.
  await assertMirrorRecordable(store, version, key);
  await putReplaceable(
    store,
    mirrorRecordKey(version, key),
    jsonBytes(record),
    'application/json'
  );
  const published = await readPlatformManifest(store, key);
  if (
    published?.version === version &&
    published.downloads[key].mainlandUrl !== url
  )
    throw new Error(
      `${key} ${version} was promoted while recording; the published mainland mirror ${published.downloads[key].mainlandUrl} stays authoritative and this record is stale`
    );
}

async function writePlatformManifest(
  store: ObjectStore,
  { version, fragment, mirror, now }: WritePlatformManifestOptions
): Promise<PlatformManifest> {
  return compareAndSwapJson<PlatformManifest>(
    store,
    platformManifestPath(fragment.platform),
    (existing) => {
      const manifest = buildPlatformManifest({
        version,
        fragment,
        mirror,
        existing,
        now
      });
      return existing?.version === version ? null : manifest;
    }
  );
}

// Copy the stored manifest so a partial promotion retry preserves the original date and mirror.
function modManifestUpdate(
  platform: string,
  manifest: PlatformManifest,
  existing: PlatformManifest | null
): PlatformManifest | null {
  validatePlatformManifest(manifest, platform);
  if (existing) {
    validatePlatformManifest(existing, platform);
    if (compareProductVersions(manifest.version, existing.version) < 0)
      throw new Error(
        `Refusing ${platform} mod release rollback from ${existing.version} to ${manifest.version}`
      );
    if (existing.version === manifest.version) {
      if (!isDeepStrictEqual(existing, manifest))
        throw new Error(
          `Published ${manifest.version} ${platform} mod manifest differs; publish a new product version`
        );
      return null;
    }
  }
  return manifest;
}

async function preflightModManifest(
  store: ObjectStore,
  platform: string,
  manifest: PlatformManifest
): Promise<void> {
  const key = modManifestPath(platform);
  modManifestUpdate(
    platform,
    manifest,
    readOptionalJson(await store.get(key), key) as PlatformManifest | null
  );
}

async function writeModManifest(
  store: ObjectStore,
  platform: string,
  manifest: PlatformManifest
): Promise<PlatformManifest> {
  return compareAndSwapJson<PlatformManifest>(
    store,
    modManifestPath(platform),
    (existing) => modManifestUpdate(platform, manifest, existing)
  );
}

async function writeLockstepManifest(
  store: ObjectStore,
  log: (line: string) => void
): Promise<{ latest: ReleaseManifest | null; advanced: boolean }> {
  const platformManifests: PlatformManifest[] = [];
  for (const platform of LOCKSTEP_PLATFORM_KEYS) {
    const manifest = await readPlatformManifest(store, platform);
    if (!manifest) {
      log(
        `${RELEASE_MANIFEST_PATH} unchanged: ${platform} has no platform manifest yet`
      );
      return { latest: null, advanced: false };
    }
    platformManifests.push(manifest);
  }
  const versions = new Set(
    platformManifests.map((manifest) => manifest.version)
  );
  if (versions.size !== 1) {
    log(
      `${RELEASE_MANIFEST_PATH} unchanged: platforms are at ${[...versions].join(' and ')}`
    );
    return { latest: null, advanced: false };
  }
  let advanced = false;
  const latest = await compareAndSwapJson<ReleaseManifest>(
    store,
    RELEASE_MANIFEST_PATH,
    (existing) => {
      const merged = mergeReleaseManifest({
        platformManifests,
        existingLatest: existing
      });
      if (existing?.version === merged.version) return null;
      advanced = true;
      return merged;
    }
  );
  return { latest, advanced };
}

export async function promotePlatform({
  version,
  platform,
  baseUrl,
  store,
  withoutMainlandMirror = false,
  log = () => {},
  releaseSignerSha256 = ANDROID_RELEASE_SIGNER_SHA256,
  now = new Date()
}: PromotePlatformOptions): Promise<PromotePlatformResult> {
  const definition = releasePlatform(platform);
  const key = definition.key;
  const lockstep = (
    LOCKSTEP_PLATFORM_KEYS as readonly ReleasePlatformKey[]
  ).includes(key);
  const fragment = await readFragment(store, version, key);
  await assertArtifactsStored(store, fragment, baseUrl);
  await assertSameCommitAcrossPlatforms(store, version, fragment);
  if (definition.kind === 'android')
    await assertAndroidSigner(
      store,
      key,
      (fragment as AndroidPlatformFragment).app.signerSha256,
      releaseSignerSha256
    );
  const existing = await readPlatformManifest(store, key);
  const mirrors = await readMirrors(store, version, [fragment], {
    withoutMainlandMirror,
    confirming: () => existing?.version === version,
    log
  });
  const candidate = buildPlatformManifest({
    version,
    fragment,
    mirror: mirrors[key],
    existing,
    now
  });
  await preflightModManifest(store, key, candidate);
  const manifest = await writePlatformManifest(store, {
    version,
    fragment,
    mirror: mirrors[key] ?? null,
    now
  });
  await writeModManifest(store, key, manifest);
  if (!lockstep) return { platform: manifest, latest: null, advanced: false };
  const latest = await writeLockstepManifest(store, log);
  return {
    platform: manifest,
    latest: latest.latest,
    advanced: latest.advanced
  };
}

export async function promoteRelease({
  version,
  baseUrl,
  store,
  withoutMainlandMirror = false,
  log = () => {},
  now = new Date()
}: PromoteReleaseOptions): Promise<ReleaseManifest> {
  const fragments = await Promise.all(
    LOCKSTEP_PLATFORM_KEYS.map((platform) =>
      readFragment<DesktopPlatformFragment>(store, version, platform)
    )
  );
  buildLatestManifest({ version, fragments, existingLatest: null, now });
  for (const fragment of fragments)
    await assertArtifactsStored(store, fragment, baseUrl);
  const existingLatest = await readLatest(store);
  const existingPlatforms: Record<string, DesktopPlatformManifest | null> = {};
  for (const platform of LOCKSTEP_PLATFORM_KEYS)
    existingPlatforms[platform] =
      await readPlatformManifest<DesktopPlatformManifest>(store, platform);
  if (existingLatest?.version === version) {
    const confirmed: [string, PlatformManifest][] = [];
    for (const fragment of fragments) {
      const platform = fragment.platform;
      const split = {
        ...existingLatest,
        platforms: { [platform]: existingLatest.platforms[platform] },
        downloads: { [platform]: existingLatest.downloads[platform] }
      };
      validatePlatformManifest(split, platform);
      buildPlatformManifest({ version, fragment, existing: split, now });
      const current = existingPlatforms[platform];
      // Confirming an older lockstep release must not move a platform that
      // has since advanced. Its newer promotion owns the mod endpoint too.
      if (current && compareProductVersions(current.version, version) > 0)
        continue;
      const manifest = current?.version === version ? current : split;
      buildPlatformManifest({ version, fragment, existing: manifest, now });
      await preflightModManifest(store, platform, manifest);
      confirmed.push([platform, manifest]);
    }
    for (const [platform, manifest] of confirmed)
      await writeModManifest(store, platform, manifest);
    return existingLatest;
  }
  const mirrors = await readMirrors(store, version, fragments, {
    withoutMainlandMirror,
    confirming: (platform) => existingPlatforms[platform]?.version === version,
    log
  });
  const platformManifests = fragments.map((fragment) =>
    buildPlatformManifest({
      version,
      fragment,
      mirror: mirrors[fragment.platform] ?? null,
      existing: existingPlatforms[fragment.platform],
      now
    })
  );
  mergeReleaseManifest({ platformManifests, existingLatest });
  for (const [index, fragment] of fragments.entries())
    await preflightModManifest(
      store,
      fragment.platform,
      platformManifests[index]
    );
  // Write platform manifests first so clients can find them as soon as latest.json advances.
  const stored: PlatformManifest[] = [];
  for (const fragment of fragments) {
    const manifest = await writePlatformManifest(store, {
      version,
      fragment,
      mirror: mirrors[fragment.platform] ?? null,
      now
    });
    await writeModManifest(store, fragment.platform, manifest);
    stored.push(manifest);
  }
  return compareAndSwapJson<ReleaseManifest>(
    store,
    RELEASE_MANIFEST_PATH,
    (existing) => {
      const latest = mergeReleaseManifest({
        platformManifests: stored,
        existingLatest: existing
      });
      return existing?.version === version ? null : latest;
    }
  );
}
