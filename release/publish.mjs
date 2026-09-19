import fs from 'node:fs';
import crypto from 'node:crypto';
import path from 'node:path';
import { readProductVersion } from './product.mjs';
import { putImmutable } from './r2-store.mjs';
import { buildPlatformFragment, buildLatestManifest } from './manifest.mjs';
import { RELEASE_PLATFORMS } from './release-platforms.mjs';
import {
  artifactManifestPath,
  validateArtifactManifest
} from './artifact-manifest.mjs';

const jsonBytes = (value) => Buffer.from(`${JSON.stringify(value, null, 2)}\n`);
function readJsonObject(object, description) {
  if (!object)
    throw new Error(
      `Missing ${description}; upload every platform before promotion`
    );
  try {
    return JSON.parse(object.bytes.toString('utf8'));
  } catch {
    throw new Error(`Invalid JSON in ${description}`);
  }
}

function objectKey(url, baseUrl) {
  const base = `${baseUrl.replace(/\/$/, '')}/`;
  if (!url.startsWith(base))
    throw new Error('Release artifact has an unexpected origin');
  return decodeURIComponent(url.slice(base.length));
}

export async function uploadPlatform({
  workspaceRoot,
  rootDir = path.join(workspaceRoot, 'bazaarplusplus-installer'),
  platform,
  baseUrl,
  store,
  validateManifest = validateArtifactManifest
}) {
  const version = readProductVersion(workspaceRoot);
  const validated = validateManifest({
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
  for (const { key, bytes } of uploads) {
    await putImmutable(store, key, bytes);
  }
  await putImmutable(
    store,
    `${version}/${fragment.platform}/updater/platform-manifest.json`,
    jsonBytes(fragment),
    'application/json'
  );
  return fragment;
}

export async function promoteRelease({
  version,
  baseUrl,
  store,
  now = new Date(),
  maxAttempts = 4
}) {
  const fragments = await Promise.all(
    RELEASE_PLATFORMS.map(async ({ key }) =>
      readJsonObject(
        await store.get(`${version}/${key}/updater/platform-manifest.json`),
        `${key} platform fragment`
      )
    )
  );
  // Validate the complete set before making any remote write.
  buildLatestManifest({ version, fragments, existingLatest: null, now });
  for (const fragment of fragments) {
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
  for (let attempt = 0; attempt < maxAttempts; attempt++) {
    const current = await store.get('latest.json');
    const existingLatest = current
      ? readJsonObject(current, 'latest.json')
      : null;
    const latest = buildLatestManifest({
      version,
      fragments,
      existingLatest,
      now
    });
    if (existingLatest?.version === version) return latest;
    try {
      if (
        await store.put('latest.json', jsonBytes(latest), {
          ifMatch: current?.etag,
          ifNoneMatch: !current,
          contentType: 'application/json'
        })
      )
        return latest;
    } catch (error) {
      const observed = await store.get('latest.json');
      if (observed?.bytes.equals(jsonBytes(latest))) return latest;
      throw error;
    }
    // A competing writer won. Re-read and re-evaluate rollback/immutability.
  }
  throw new Error(
    'Release promotion conflicted repeatedly; retry after the other publisher finishes'
  );
}
