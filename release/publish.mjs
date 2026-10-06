import fs from 'node:fs';
import crypto from 'node:crypto';
import path from 'node:path';
import { compareProductVersions, readProductVersion } from './product.mjs';
import { putImmutable, putReplaceable } from './r2-store.mjs';
import {
  RELEASE_MANIFEST_PATH,
  assertMirrorUrl,
  buildLatestManifest,
  buildMirrorRecord,
  buildPlatformFragment,
  buildPlatformManifest,
  fragmentKey,
  installerFileName,
  mergeReleaseManifest,
  mirrorRecordKey,
  platformManifestPath,
  validateMirrorRecord,
  validatePlatformFragment,
  validatePlatformManifest
} from './manifest.mjs';
import { assertMainlandMirrors, checkMainlandMirror } from './mirror.mjs';
import {
  RELEASE_PLATFORMS,
  RELEASE_PLATFORM_KEYS,
  releasePlatform
} from './release-platforms.mjs';
import {
  artifactManifestPath,
  validateArtifactManifest
} from './artifact-manifest.mjs';

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

// A product version names one commit on every platform, across time as well
// as across hosts: a platform may ship a version later, but only that commit.
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
  for (const record of [
    fragment.installer,
    fragment.updater,
    fragment.signatureFile
  ]) {
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
  const key = releasePlatform(platform).key;
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

// Preflight the complete release before recording either mirror. The two
// conditional writes are retryable, not a transaction across R2 objects.
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
  for (const { buildPlatform } of RELEASE_PLATFORMS)
    assertMirrorUrl(urls?.[buildPlatform]);
  const fragments = await Promise.all(
    RELEASE_PLATFORM_KEYS.map((key) => readFragment(store, version, key))
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
        url: urls[RELEASE_PLATFORMS[index].buildPlatform],
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

// Advance the Release Manifest when every platform manifest names the same
// release; otherwise leave it at the last lockstep release.
async function writeLockstepManifest(store, log) {
  const platformManifests = [];
  for (const platform of RELEASE_PLATFORM_KEYS) {
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

// Promote one platform without waiting for the other. The lockstep Release
// Manifest advances only once every platform names the same release; the
// per-platform endpoint bootstrap finished at 5.5.0 (ADR 0003).
export async function promotePlatform({
  version,
  platform,
  baseUrl,
  store,
  withoutMainlandMirror = false,
  log = () => {},
  now = new Date()
}) {
  const key = releasePlatform(platform).key;
  const fragment = await readFragment(store, version, key);
  await assertArtifactsStored(store, fragment, baseUrl);
  await assertSameCommitAcrossPlatforms(store, version, fragment);
  const existing = await readPlatformManifest(store, key);
  const mirrors = await readMirrors(store, version, [fragment], {
    withoutMainlandMirror,
    confirming: () => existing?.version === version,
    log
  });
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
  const lockstep = await writeLockstepManifest(store, log);
  return {
    platform: manifest,
    latest: lockstep.latest,
    advanced: lockstep.advanced
  };
}

// Promote every platform at once: the ordinary lockstep release.
export async function promoteRelease({
  version,
  baseUrl,
  store,
  withoutMainlandMirror = false,
  log = () => {},
  now = new Date()
}) {
  const fragments = await Promise.all(
    RELEASE_PLATFORM_KEYS.map((platform) =>
      readFragment(store, version, platform)
    )
  );
  // Validate the complete set before making any remote write.
  buildLatestManifest({ version, fragments, existingLatest: null, now });
  for (const fragment of fragments)
    await assertArtifactsStored(store, fragment, baseUrl);
  const existingLatest = await readLatest(store);
  const existingPlatforms = {};
  for (const platform of RELEASE_PLATFORM_KEYS)
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
