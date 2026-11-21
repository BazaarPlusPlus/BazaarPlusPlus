import crypto from 'node:crypto';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { Readable } from 'node:stream';
import { pipeline } from 'node:stream/promises';
import { sha256File } from './android-apk.ts';
import {
  fragmentKey,
  installerFileName,
  mirrorArtifact,
  mirrorFileName,
  mirrorRecordKey,
  platformManifestPath,
  validateMirrorRecord,
  validatePlatformFragment,
  validatePlatformManifest
} from './manifest.ts';
import type {
  MirrorRecord,
  PlatformFragment,
  PlatformManifest,
  ReleaseArtifact
} from './manifest.ts';
import { assertProductVersion } from './product.ts';
import type { ProductVersion } from './product.ts';
import type { FetchImpl } from './r2-store.ts';
import {
  LOCKSTEP_PLATFORM_KEYS,
  RELEASE_PLATFORM_KEYS,
  releasePlatform
} from './release-platforms.ts';
import type { ReleasePlatformKey } from './release-platforms.ts';

// The self-hosted Mainland Mirror (docs/adr/0002-mainland-mirror-check.md).
// Its authorized_keys forces `rrsync -wo -no-del -no-overwrite` on the served
// root: remote paths are relative to it, and an existing file is skipped while
// rsync still exits 0, so only the byte check after a push catches a wrong one.
export const MAINLAND_MIRROR = Object.freeze({
  baseUrl: 'https://1.14.150.142/bazaarplusplus',
  sshTarget: 'bpp-mirror@1.14.150.142',
  hostKey:
    '1.14.150.142 ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAII5gPFM50Rx1AaeL5aic3VbTeYintL6QefTEUMX6c0Dj'
});

export type MirrorOutcome =
  | 'verified'
  | 'missing-or-different'
  | 'unverifiable'
  | 'waived';

export interface MirrorClassification {
  outcome: Exclude<MirrorOutcome, 'waived'>;
  detail: string;
}

export interface MirrorCheckResult {
  platform: string;
  url: string | null;
  fileName: string;
  outcome: MirrorOutcome;
  detail: string;
}

export type RunRsync = (args: readonly string[]) => Promise<void> | void;

export interface PushMainlandMirrorOptions {
  version: ProductVersion;
  fragment: PlatformFragment;
  sshKey: string;
  fetchImpl?: FetchImpl;
  runRsync?: RunRsync;
}

export interface CheckMainlandMirrorOptions {
  platform: string;
  url: string;
  fileName: string;
  artifact: Pick<ReleaseArtifact, 'size' | 'sha256'>;
  fetchImpl?: FetchImpl;
}

export interface AssertMainlandMirrorsOptions {
  allowUnverified?: boolean;
  log?: (line: string) => void;
  remedy?: string;
}

export interface VerifyMainlandMirrorsOptions {
  baseUrl: string;
  version?: ProductVersion | null;
  platform?: string;
  latest?: boolean;
  fetchImpl?: FetchImpl;
  log?: (line: string) => void;
}

export interface MirrorVerification {
  version: string;
  results: MirrorCheckResult[];
}

interface MirrorTarget {
  platform: string;
  url: string | null;
  fileName: string;
  artifact: ReleaseArtifact;
}

const JSON_TIMEOUT_MS = 20_000;
const JSON_MAX_BYTES = 512 * 1024;
// The mirror serves about 4 Mbit/s: the 67 MB Android App APK takes 2-3 minutes.
const TRANSFER_TIMEOUT_MS = 10 * 60_000;

export function mainlandMirrorUrl(
  version: ProductVersion,
  fileName: string
): string {
  return `${MAINLAND_MIRROR.baseUrl}/${version}/${encodeURIComponent(fileName)}`;
}

function runRsync(args: readonly string[]): void {
  const result = spawnSync('rsync', args, { stdio: 'inherit' });
  if (result.error)
    throw new Error(`Cannot start rsync: ${result.error.message}`);
  if (result.status !== 0)
    throw new Error(
      `rsync to the mainland mirror failed (${result.signal ?? `exit ${result.status}`})`
    );
}

async function download(
  artifact: ReleaseArtifact,
  file: string,
  fetchImpl: FetchImpl
): Promise<void> {
  const response = await fetchImpl(artifact.url, {
    headers: {},
    signal: AbortSignal.timeout(TRANSFER_TIMEOUT_MS)
  });
  if (response.status !== 200 || !response.body) {
    await response.body?.cancel();
    throw new Error(`${artifact.url} responded with HTTP ${response.status}`);
  }
  await pipeline(Readable.fromWeb(response.body), fs.createWriteStream(file));
  const size = fs.statSync(file).size;
  const sha256 = await sha256File(file);
  if (size !== artifact.size || sha256 !== artifact.sha256)
    throw new Error(
      `${artifact.url} is ${size} bytes with sha256 ${sha256}; the fragment records ${artifact.size} bytes with sha256 ${artifact.sha256}`
    );
}

// Downloads the release artifact, proves it is the uploaded one, and pushes it
// as <version>/<fileName>; the key and known_hosts live only for the push.
export async function pushMainlandMirror({
  version,
  fragment,
  sshKey,
  fetchImpl = fetch,
  runRsync: run = runRsync
}: PushMainlandMirrorOptions): Promise<void> {
  const work = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-mirror-'));
  try {
    const staging = path.join(work, 'staging', version);
    fs.mkdirSync(staging, { recursive: true });
    await download(
      mirrorArtifact(fragment),
      path.join(staging, mirrorFileName(fragment)),
      fetchImpl
    );
    const key = path.join(work, 'key');
    const knownHosts = path.join(work, 'known_hosts');
    // OpenSSH refuses a private key without its final newline.
    fs.writeFileSync(key, sshKey.endsWith('\n') ? sshKey : `${sshKey}\n`, {
      mode: 0o600
    });
    fs.writeFileSync(knownHosts, `${MAINLAND_MIRROR.hostKey}\n`);
    // openrsync rejects --chmod; without -p the server creates 0644 files.
    await run([
      '-rt',
      '-e',
      `ssh -i ${key} -o UserKnownHostsFile=${knownHosts} -o StrictHostKeyChecking=yes -o IdentitiesOnly=yes -o BatchMode=yes`,
      staging,
      `${MAINLAND_MIRROR.sshTarget}:`
    ]);
  } finally {
    fs.rmSync(work, { recursive: true, force: true });
  }
}

async function classifyMirror(
  url: string,
  { size, sha256 }: Pick<ReleaseArtifact, 'size' | 'sha256'>,
  fetchImpl: FetchImpl
): Promise<MirrorClassification> {
  try {
    const response = await fetchImpl(url, {
      headers: {},
      signal: AbortSignal.timeout(TRANSFER_TIMEOUT_MS)
    });
    if (response.status !== 200) {
      await response.body?.cancel();
      return response.status === 404
        ? { outcome: 'missing-or-different', detail: 'HTTP 404' }
        : { outcome: 'unverifiable', detail: `HTTP ${response.status}` };
    }
    const length = response.headers.get('content-length');
    if (length !== null && Number(length) !== size) {
      await response.body?.cancel();
      return {
        outcome: 'missing-or-different',
        detail: `serves ${length} bytes, expected ${size}`
      };
    }
    const hash = crypto.createHash('sha256');
    let served = 0;
    if (response.body)
      for await (const chunk of response.body) {
        served += chunk.length;
        hash.update(chunk);
      }
    const digest = hash.digest('hex');
    if (served !== size)
      return {
        outcome: 'missing-or-different',
        detail: `serves ${served} bytes, expected ${size}`
      };
    if (digest !== sha256)
      return {
        outcome: 'missing-or-different',
        detail: `serves sha256 ${digest}, expected ${sha256}`
      };
    return {
      outcome: 'verified',
      detail: `serves ${size} bytes with sha256 ${sha256}`
    };
  } catch (error) {
    return {
      outcome: 'unverifiable',
      detail: `request failed: ${error instanceof Error ? error.message : String(error)}`
    };
  }
}

export async function checkMainlandMirror({
  platform,
  url,
  fileName,
  artifact,
  fetchImpl = fetch
}: CheckMainlandMirrorOptions): Promise<MirrorCheckResult> {
  return {
    platform,
    url,
    fileName,
    ...(await classifyMirror(url, artifact, fetchImpl))
  };
}

export function describeMirrorResult(result: MirrorCheckResult): string {
  return `${result.platform}: ${result.outcome} ${result.url} (${result.detail})`;
}

const RECORD_REMEDY =
  're-run mirror once the server serves the release bytes (it never overwrites a file; remove a wrong one by hand), or pass --allow-unverified-mirror';
export const VERIFY_REMEDY =
  'make the mirror server serve the release bytes at that address (it never overwrites a file; remove a wrong one by hand), and re-run mirror if the version is not promoted yet';

export function assertMainlandMirrors(
  results: MirrorCheckResult[],
  {
    allowUnverified = false,
    log = () => {},
    remedy = RECORD_REMEDY
  }: AssertMainlandMirrorsOptions = {}
): MirrorCheckResult[] {
  // A platform promoted without a mirror is reported, never failed: the
  // waiver was explicit at promotion time.
  const failed = results.filter(
    (result) => result.outcome !== 'verified' && result.outcome !== 'waived'
  );
  if (failed.length === 0) return results;
  const lines = failed.map(describeMirrorResult);
  if (!allowUnverified)
    throw new Error(
      `Mainland mirror check failed; ${remedy}:\n${lines.join('\n')}`
    );
  for (const line of lines)
    log(`WARNING: recording an unverified mainland mirror: ${line}`);
  return results;
}

async function readBounded(
  response: Response,
  maxBytes: number
): Promise<string> {
  if (!response.body) return '';
  const chunks: Buffer[] = [];
  let size = 0;
  for await (const chunk of response.body) {
    size += chunk.length;
    if (size > maxBytes) throw new Error('response exceeds expected size');
    chunks.push(Buffer.from(chunk));
  }
  return Buffer.concat(chunks).toString('utf8');
}

async function fetchJson(
  url: string,
  fetchImpl: FetchImpl,
  description: string,
  { missing }: { missing?: string } = {}
): Promise<unknown> {
  const response = await fetchImpl(url, {
    headers: { accept: 'application/json' },
    signal: AbortSignal.timeout(JSON_TIMEOUT_MS)
  });
  if (response.status === 404) {
    await response.body?.cancel();
    throw new Error(missing ?? `Missing ${description}`);
  }
  if (response.status !== 200) {
    await response.body?.cancel();
    throw new Error(`${description} responded with HTTP ${response.status}`);
  }
  try {
    return JSON.parse(await readBounded(response, JSON_MAX_BYTES));
  } catch {
    throw new Error(`Invalid JSON in ${description}`);
  }
}

// Before promotion, name Android explicitly because it uploads independently of the Lockstep Platforms.
function platformKeysFor(
  platform: string | undefined,
  latest: boolean
): readonly ReleasePlatformKey[] {
  if (platform) return [releasePlatform(platform).key];
  return latest ? RELEASE_PLATFORM_KEYS : LOCKSTEP_PLATFORM_KEYS;
}

// Checks the recorded address, whatever its host: 6.4.3 was promoted with a mirror on another server.
export async function verifyMainlandMirrors({
  baseUrl,
  version = null,
  platform = undefined,
  latest = false,
  fetchImpl = fetch,
  log = () => {}
}: VerifyMainlandMirrorsOptions): Promise<MirrorVerification> {
  const origin = baseUrl.replace(/\/$/, '');
  const targets: MirrorTarget[] = [];
  let target = version;
  if (latest) {
    const versions: ProductVersion[] = [];
    for (const key of platformKeysFor(platform, true)) {
      const manifestPath = platformManifestPath(key);
      const manifest = validatePlatformManifest(
        (await fetchJson(`${origin}/${manifestPath}`, fetchImpl, manifestPath, {
          missing: `${key} has no platform manifest yet at ${manifestPath}`
        })) as PlatformManifest,
        key
      );
      versions.push(manifest.version);
      const download = manifest.downloads[key];
      const artifact = download.app === undefined ? download : download.app;
      targets.push({
        platform: key,
        url: download.mainlandUrl ?? null,
        fileName: installerFileName(artifact.url),
        artifact
      });
    }
    target = [...new Set(versions)].join(' / ');
  } else {
    target = assertProductVersion(target);
    for (const key of platformKeysFor(platform, false)) {
      const uploadedKey = fragmentKey(target, key);
      const fragment = validatePlatformFragment(
        (await fetchJson(`${origin}/${uploadedKey}`, fetchImpl, uploadedKey, {
          missing: `${key} ${target} is not uploaded yet; run upload first`
        })) as PlatformFragment,
        target,
        key
      );
      const fileName = mirrorFileName(fragment);
      const recordKey = mirrorRecordKey(target, key);
      const record = validateMirrorRecord(
        (await fetchJson(`${origin}/${recordKey}`, fetchImpl, recordKey, {
          missing: `${key} ${target} has no mainland mirror recorded; run mirror first`
        })) as MirrorRecord,
        target,
        key,
        fileName
      );
      targets.push({
        platform: key,
        url: record.url,
        fileName,
        artifact: mirrorArtifact(fragment)
      });
    }
  }
  const results: MirrorCheckResult[] = [];
  for (const { url, ...entry } of targets)
    results.push(
      url
        ? await checkMainlandMirror({ ...entry, url, fetchImpl })
        : {
            platform: entry.platform,
            url,
            fileName: entry.fileName,
            outcome: 'waived',
            detail: 'promoted without a mainland mirror'
          }
    );
  for (const result of results) log(describeMirrorResult(result));
  assertMainlandMirrors(results, { remedy: VERIFY_REMEDY });
  return { version: target, results };
}
