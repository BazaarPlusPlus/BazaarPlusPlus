import fs from 'node:fs';
import crypto from 'node:crypto';
import path from 'node:path';
import { compareProductVersions, readProductVersion } from './product.mjs';
import { putImmutable, putImmutableFile, putReplaceable } from './r2-store.mjs';
import {
  RELEASE_MANIFEST_PATH,
  assertMirrorUrl,
  buildAndroidFragment,
  buildLatestManifest,
  buildMirrorRecord,
  buildPlatformFragment,
  buildPlatformManifest,
  fragmentArtifacts,
  fragmentKey,
  installerFileName,
  managedPayloadKey,
  mergeReleaseManifest,
  mirrorRecordKey,
  platformManifestPath,
  validateMirrorRecord,
  validatePlatformFragment,
  validatePlatformManifest
} from './manifest.mjs';
import { assertMainlandMirrors, checkMainlandMirror } from './mirror.mjs';
import {
  LOCKSTEP_PLATFORM_KEYS,
  RELEASE_PLATFORM_KEYS,
  releasePlatform,
  releasePlatformByKey
} from './release-platforms.mjs';
import {
  artifactManifestPath,
  gitStateForRoot,
  validateArtifactManifest
} from './artifact-manifest.mjs';
import {
  androidApkRecordKey,
  publishedAndroidApk,
  readAndroidArtifacts
} from './android-release.mjs';

const jsonBytes = (value) => Buffer.from(`${JSON.stringify(value, null, 2)}\n`);
function readJsonObject(object, description) {
  if (!object)
    throw new Error(`Missing ${description}; run upload for it first`);
  try {
    return JSON.parse(object.bytes.toString('utf8'));
  } catch {
    throw new Error(`Invalid JSON in ${description}`);
  }
}

function readOptionalJson(object, description) {
  return object ? readJsonObject(object, description) : null;
}

async function readFragment(store, version, platform) {
  return validatePlatformFragment(
    readJsonObject(
      await store.get(fragmentKey(version, platform)),
      `${platform} platform fragment`
    ),
    version,
    platform
  );
}

async function readPlatformManifest(store, platform) {
  const object = await store.get(platformManifestPath(platform));
  return object
    ? validatePlatformManifest(
        readJsonObject(object, `${platform} platform manifest`),
        platform
      )
    : null;
}

async function readLatest(store) {
  return readOptionalJson(
    await store.get(RELEASE_MANIFEST_PATH),
    RELEASE_MANIFEST_PATH
  );
}

// A product version names one commit on every platform, Android included,
// across time as well as across hosts: a platform may ship a version later,
// but only that commit.
async function assertSameCommitAcrossPlatforms(store, version, fragment) {
  for (const platform of RELEASE_PLATFORM_KEYS) {
    if (platform === fragment.platform) continue;
    const other = readOptionalJson(
      await store.get(fragmentKey(version, platform)),
      `${platform} platform fragment`
    );
    if (other && other.gitCommit !== fragment.gitCommit)
      throw new Error(
        `${version} was uploaded from commit ${other.gitCommit} on ${platform}; build that commit or publish a new product version`
      );
  }
}

async function assertArtifactsStored(store, fragment, baseUrl) {
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

// Mirror records are read once per promotion. A platform whose manifest is
// already at this version is being confirmed: its published address is
// authoritative, so its record is not even read.
async function readMirrors(
  store,
  version,
  fragments,
  { withoutMainlandMirror, confirming, log }
) {
  const mirrors = {};
  for (const fragment of fragments) {
    if (confirming(fragment.platform)) continue;
    const record = readOptionalJson(
      await store.get(mirrorRecordKey(version, fragment.platform)),
      `${fragment.platform} mainland mirror record`
    );
    if (record) {
      mirrors[fragment.platform] = validateMirrorRecord(
        record,
        version,
        fragment.platform,
        installerFileName(fragment.installer.url)
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

// ETag compare-and-swap of one JSON object. `build(existing)` returns the
// next value, or null to keep the existing one; a lost PUT response is
// recognized by reading the object back.
async function compareAndSwapJson(store, key, build, maxAttempts = 4) {
  for (let attempt = 0; attempt < maxAttempts; attempt++) {
    const current = await store.get(key);
    const existing = readOptionalJson(current, key);
    const next = build(existing);
    if (next === null) return existing;
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
    // A competing writer won. Re-read and re-evaluate rollback/immutability.
  }
  throw new Error(
    `Writing ${key} conflicted repeatedly; retry after the other publisher finishes`
  );
}

function objectKey(url, baseUrl) {
  const base = `${baseUrl.replace(/\/$/, '')}/`;
  if (!url.startsWith(base))
    throw new Error('Release artifact has an unexpected origin');
  return decodeURIComponent(url.slice(base.length));
}

export async function uploadPlatform({
  workspaceRoot,
  platform,
  baseUrl,
  store
}) {
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
    baseUrl
  });
  // Freeze and verify every byte before the first remote write. A concurrent
  // local build must never poison an immutable key halfway through an upload.
  const snapshots = new Map();
  const uploads = [
    [validated.installer, fragment.installer],
    [validated.updater, fragment.updater],
    [validated.signature, fragment.signatureFile]
  ].map(([file, record]) => {
    if (!snapshots.has(file)) snapshots.set(file, fs.readFileSync(file));
    const bytes = snapshots.get(file);
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
    fragmentKey(version, fragment.platform),
    jsonBytes(fragment),
    'application/json'
  );
  return fragment;
}

// The facts a published Android Release APK carries in its sidecar.
const ANDROID_APK_FACTS = Object.freeze([
  'size',
  'sha256',
  'versionCode',
  'signerSha256',
  'builtForProductVersion'
]);

// Uploads the Android build of VERSION: the Android Release APK under its own
// identity unless that identity is already published (a mod-only release then
// points at the published APK), the Managed Payload zip and its signature,
// and last the fragment naming both. Large files stream from disk.
export async function uploadAndroidPlatform({
  workspaceRoot,
  baseUrl,
  store,
  gitState = gitStateForRoot,
  log = () => {}
}) {
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
  const apkKey = manifest.apk.key;
  const identity = {
    gameVersion: manifest.game.gameVersion,
    loader: manifest.loader,
    interopSha256: manifest.interopSha256
  };
  const published = await publishedAndroidApk(store, identity);
  let apk;
  if (published) {
    if (files.apk)
      log(
        `${apkKey} is already published (built for ${published.builtForProductVersion}); the APK built here is not uploaded`
      );
    apk = published;
  } else if (!files.apk) {
    throw new Error(
      `${apkKey} was published when the Android build ran but is missing now; build again with --rebuild-apk`
    );
  } else {
    apk = Object.fromEntries(
      ANDROID_APK_FACTS.map((name) => [name, manifest.apk[name]])
    );
  }
  const fragment = buildAndroidFragment({
    version,
    gitCommit: manifest.gitCommit,
    baseUrl,
    apk: {
      size: apk.size,
      sha256: apk.sha256,
      versionCode: apk.versionCode,
      signerSha256: apk.signerSha256,
      builtForProductVersion: apk.builtForProductVersion
    },
    managedPayload: {
      size: manifest.managedPayload.size,
      sha256: manifest.managedPayload.sha256,
      signature: manifest.managedPayload.signature
    },
    // A client running an APK of an earlier product version can take this
    // release as a Managed Payload; one built here needs the new APK.
    selfUpdatable: apk.builtForProductVersion !== version,
    historyDatabaseUserVersion: manifest.historyDatabaseUserVersion,
    nonManagedPayloadDigest: manifest.nonManagedPayloadDigest,
    game: manifest.game,
    loader: manifest.loader,
    interopSha256: manifest.interopSha256
  });
  if (objectKey(fragment.apk.url, baseUrl) !== apkKey)
    throw new Error(`Android build names ${apkKey}, not ${fragment.apk.url}`);
  await assertSameCommitAcrossPlatforms(store, version, fragment);
  if (!published) {
    log(`Uploading ${apkKey} (${apk.size} bytes)`);
    await putImmutableFile(store, apkKey, files.apk, {
      sha256: apk.sha256,
      size: apk.size,
      contentType: 'application/vnd.android.package-archive'
    });
    await putImmutable(
      store,
      androidApkRecordKey(apkKey),
      jsonBytes(apk),
      'application/json'
    );
  }
  const zipKey = managedPayloadKey(version, fragment.platform);
  await putImmutableFile(store, zipKey, files.managedPayload, {
    sha256: manifest.managedPayload.sha256,
    size: manifest.managedPayload.size,
    contentType: 'application/zip'
  });
  await putImmutable(
    store,
    `${zipKey}.sig`,
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

async function assertMirrorRecordable(store, version, key) {
  for (const [source, published] of [
    [platformManifestPath(key), await readPlatformManifest(store, key)],
    [RELEASE_MANIFEST_PATH, await readLatest(store)]
  ]) {
    if (published && compareProductVersions(version, published.version) <= 0)
      throw new Error(
        `${source} has already published ${published.version}; a mainland mirror for ${key} ${version} can no longer be recorded, publish a new product version`
      );
  }
}

export async function recordMainlandMirror({
  version,
  platform,
  url,
  store,
  probeMirror,
  allowUnverified = false,
  log = () => {}
}) {
  const definition = releasePlatform(platform);
  if (definition.kind !== 'tauri')
    throw new Error(`${platform} has no mainland mirror`);
  const key = definition.key;
  if (typeof probeMirror !== 'function')
    throw new Error('Recording a mainland mirror requires a page probe');
  assertMirrorUrl(url);
  const fragment = await readFragment(store, version, key);
  await assertMirrorRecordable(store, version, key);
  const record = await prepareMirrorRecord({
    version,
    fragment,
    url,
    probeMirror,
    allowUnverified,
    log
  });
  await writeMirrorRecord(store, record);
  return record;
}

async function prepareMirrorRecord({
  version,
  fragment,
  url,
  probeMirror,
  allowUnverified,
  log
}) {
  const key = fragment.platform;
  const fileName = installerFileName(fragment.installer.url);
  const result = await checkMainlandMirror({
    platform: key,
    url,
    fileName,
    probeMirror
  });
  log(`Mainland mirror ${key}: ${result.outcome} (${result.detail})`);
  assertMainlandMirrors([result], { allowUnverified, log });
  return buildMirrorRecord({
    version,
    platform: key,
    url,
    fileName,
    verified: result.outcome === 'verified'
  });
}

async function writeMirrorRecord(store, record) {
  const { version, platform: key, url } = record;
  // The probe took time; a promotion may have published the previous record
  // meanwhile. The published address wins, so re-check before and after.
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

// Preflight the complete lockstep release before recording either mirror. The
// two conditional writes are retryable, not a transaction across R2 objects.
export async function recordMainlandMirrors({
  version,
  urls,
  baseUrl,
  store,
  probeMirror,
  allowUnverified = false,
  log = () => {}
}) {
  if (typeof probeMirror !== 'function')
    throw new Error('Recording mainland mirrors requires a page probe');
  const lockstep = LOCKSTEP_PLATFORM_KEYS.map(releasePlatformByKey);
  for (const { buildPlatform } of lockstep)
    assertMirrorUrl(urls?.[buildPlatform]);
  const fragments = await Promise.all(
    lockstep.map(({ key }) => readFragment(store, version, key))
  );
  buildLatestManifest({ version, fragments, existingLatest: null });
  for (const fragment of fragments) {
    await assertArtifactsStored(store, fragment, baseUrl);
    await assertMirrorRecordable(store, version, fragment.platform);
  }
  const records = [];
  for (const [index, fragment] of fragments.entries()) {
    records.push(
      await prepareMirrorRecord({
        version,
        fragment,
        url: urls[lockstep[index].buildPlatform],
        probeMirror,
        allowUnverified,
        log
      })
    );
  }
  // A publisher may have advanced either platform during the page probes.
  for (const record of records)
    await assertMirrorRecordable(store, version, record.platform);
  for (const record of records) await writeMirrorRecord(store, record);
  return records;
}

async function writePlatformManifest(
  store,
  { version, fragment, mirror, now }
) {
  return compareAndSwapJson(
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

// Advance the Release Manifest when every Lockstep Platform manifest names the
// same release; otherwise leave it at the last lockstep release.
async function writeLockstepManifest(store, log) {
  const platformManifests = [];
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
  const latest = await compareAndSwapJson(
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

// Promote one platform without waiting for the others. The lockstep Release
// Manifest advances only once every Lockstep Platform names the same release
// (ADR 0003). Android writes only its Platform Release Manifest and records
// no mainland mirror (ADR 0010).
export async function promotePlatform({
  version,
  platform,
  baseUrl,
  store,
  withoutMainlandMirror = false,
  log = () => {},
  now = new Date()
}) {
  const definition = releasePlatform(platform);
  const key = definition.key;
  const lockstep = LOCKSTEP_PLATFORM_KEYS.includes(key);
  if (withoutMainlandMirror && definition.kind !== 'tauri')
    throw new Error(`${platform} has no mainland mirror to waive`);
  const fragment = await readFragment(store, version, key);
  await assertArtifactsStored(store, fragment, baseUrl);
  await assertSameCommitAcrossPlatforms(store, version, fragment);
  const existing = await readPlatformManifest(store, key);
  const mirrors =
    definition.kind === 'tauri'
      ? await readMirrors(store, version, [fragment], {
          withoutMainlandMirror,
          confirming: () => existing?.version === version,
          log
        })
      : {};
  // Validate against the current manifest before any remote write.
  buildPlatformManifest({
    version,
    fragment,
    mirror: mirrors[key],
    existing,
    now
  });
  const manifest = await writePlatformManifest(store, {
    version,
    fragment,
    mirror: mirrors[key] ?? null,
    now
  });
  if (!lockstep) return { platform: manifest, latest: null, advanced: false };
  const latest = await writeLockstepManifest(store, log);
  return {
    platform: manifest,
    latest: latest.latest,
    advanced: latest.advanced
  };
}

// Promote every Lockstep Platform at once: the ordinary desktop release.
export async function promoteRelease({
  version,
  baseUrl,
  store,
  withoutMainlandMirror = false,
  log = () => {},
  now = new Date()
}) {
  const fragments = await Promise.all(
    LOCKSTEP_PLATFORM_KEYS.map((platform) =>
      readFragment(store, version, platform)
    )
  );
  // Validate the complete set before making any remote write.
  buildLatestManifest({ version, fragments, existingLatest: null, now });
  for (const fragment of fragments)
    await assertArtifactsStored(store, fragment, baseUrl);
  const existingLatest = await readLatest(store);
  const existingPlatforms = {};
  for (const platform of LOCKSTEP_PLATFORM_KEYS)
    existingPlatforms[platform] = await readPlatformManifest(store, platform);
  if (existingLatest?.version === version) {
    // Confirming a published release: the Release Manifest is authoritative
    // and each fragment must still match it.
    for (const fragment of fragments) {
      const platform = fragment.platform;
      const split = {
        ...existingLatest,
        platforms: { [platform]: existingLatest.platforms[platform] },
        downloads: { [platform]: existingLatest.downloads[platform] }
      };
      validatePlatformManifest(split, platform);
      buildPlatformManifest({ version, fragment, existing: split, now });
    }
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
  // Platform manifests first: a client that installs this release through
  // the lockstep manifest reads its platform manifest next and must find it.
  // The stored manifests, not this promoter's copies, feed the merge.
  const stored = [];
  for (const fragment of fragments) {
    stored.push(
      await writePlatformManifest(store, {
        version,
        fragment,
        mirror: mirrors[fragment.platform] ?? null,
        now
      })
    );
  }
  return compareAndSwapJson(store, RELEASE_MANIFEST_PATH, (existing) => {
    const latest = mergeReleaseManifest({
      platformManifests: stored,
      existingLatest: existing
    });
    return existing?.version === version ? null : latest;
  });
}
