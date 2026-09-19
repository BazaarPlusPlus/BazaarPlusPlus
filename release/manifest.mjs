import path from 'node:path';
import { isDeepStrictEqual } from 'node:util';
import { assertProductVersion, compareProductVersions } from './product.mjs';
import { RELEASE_PLATFORMS } from '../bazaarplusplus-installer/scripts/release/release-platforms.mjs';

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

export function buildPlatformFragment({ manifest, baseUrl }) {
  const version = assertProductVersion(manifest.appVersion);
  const definition = RELEASE_PLATFORMS.find(
    (platform) =>
      platform.key === manifest.releasePlatformKey &&
      platform.buildPlatform === manifest.buildPlatform
  );
  if (
    !definition ||
    manifest.dirty ||
    !/^[a-f0-9]{40,64}$/.test(manifest.gitCommit ?? '')
  )
    throw new Error(
      'Platform fragment requires a clean, identified product build'
    );
  const base = new URL(baseUrl);
  if (
    base.protocol !== 'https:' ||
    base.username ||
    base.password ||
    base.search ||
    base.hash
  )
    throw new Error('Unsafe release origin');
  const record = (artifact, category) => ({
    url: `${baseUrl.replace(/\/$/, '')}/${version}/${definition.key}/${category}/${encodeURIComponent(path.basename(artifact.path))}`,
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
    signatureFile: record(manifest.signature, 'updater')
  };
  validatePlatformFragment(fragment, version, definition.key);
  return fragment;
}

export function validatePlatformFragment(fragment, version, platform) {
  if (
    fragment?.schemaVersion !== 1 ||
    fragment.version !== version ||
    fragment.platform !== platform ||
    !/^[a-f0-9]{40,64}$/.test(fragment.gitCommit ?? '')
  )
    throw new Error(`Invalid ${platform} platform fragment for ${version}`);
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
    if (!url.pathname.startsWith(`/${version}/${platform}/${category}/`))
      throw new Error(`Artifact URL is outside its release: ${record.url}`);
  }
  return fragment;
}

export function buildLatestManifest({
  version,
  fragments,
  existingLatest,
  now = new Date()
}) {
  assertProductVersion(version);
  if (
    existingLatest &&
    compareProductVersions(version, existingLatest.version) < 0
  )
    throw new Error(
      `Refusing release rollback from ${existingLatest.version} to ${version}`
    );
  if (
    !Array.isArray(fragments) ||
    fragments.length !== RELEASE_PLATFORMS.length
  )
    throw new Error(`Release ${version} requires every declared platform`);
  const byPlatform = new Map(
    fragments.map((fragment) => [fragment.platform, fragment])
  );
  const ordered = RELEASE_PLATFORMS.map(({ key }) =>
    validatePlatformFragment(byPlatform.get(key), version, key)
  );
  if (new Set(ordered.map((fragment) => fragment.gitCommit)).size !== 1)
    throw new Error('Release platform commits differ');
  const reuse = existingLatest?.version === version;
  const latest = {
    version,
    gitCommit: ordered[0].gitCommit,
    notes: reuse ? existingLatest.notes : `Release ${version}`,
    pub_date: reuse ? existingLatest.pub_date : now.toISOString(),
    platforms: {},
    downloads: {}
  };
  for (const fragment of ordered) {
    latest.platforms[fragment.platform] = {
      url: fragment.updater.url,
      signature: fragment.updater.signature
    };
    latest.downloads[fragment.platform] = fragment.installer;
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
