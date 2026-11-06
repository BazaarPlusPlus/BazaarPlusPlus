import {
  fragmentKey,
  installerFileName,
  mirrorRecordKey,
  platformManifestPath,
  validateMirrorRecord,
  validatePlatformFragment,
  validatePlatformManifest
} from './manifest.ts';
import type {
  DesktopPlatformFragment,
  DesktopPlatformManifest,
  MirrorRecord
} from './manifest.ts';
import { assertProductVersion } from './product.ts';
import type { ProductVersion } from './product.ts';
import type { FetchImpl } from './r2-store.ts';
import {
  LOCKSTEP_PLATFORM_KEYS,
  releasePlatform
} from './release-platforms.ts';
import type { ReleasePlatformKey } from './release-platforms.ts';

/** A probed share page: its status and body, or the failure of the request. */
export type MirrorPage =
  | { status: number; html: string; error?: undefined }
  | { error: Error; status?: undefined; html?: undefined };

/** How a mirror check ended; `waived` marks a release without a mirror. */
export type MirrorOutcome =
  'verified' | 'missing-or-misnamed' | 'unverifiable' | 'waived';

/** What classifyMirrorPage concludes from one page. */
export interface MirrorClassification {
  outcome: Exclude<MirrorOutcome, 'waived'>;
  detail: string;
}

/** The outcome of one platform's mirror check. */
export interface MirrorCheckResult {
  platform: string;
  url: string | null;
  fileName: string;
  outcome: MirrorOutcome;
  detail: string;
}

/** Reads a share page; fetchMirrorPage in production. */
export type ProbeMirror = (url: string) => Promise<MirrorPage>;

/** The options of fetchMirrorPage. */
export interface FetchMirrorPageOptions {
  fetchImpl?: FetchImpl;
  timeoutMs?: number;
}

/** The inputs of checkMainlandMirror. */
export interface CheckMainlandMirrorOptions {
  platform: string;
  url: string;
  fileName: string;
  probeMirror: ProbeMirror;
}

/** The options of assertMainlandMirrors. */
export interface AssertMainlandMirrorsOptions {
  allowUnverified?: boolean;
  log?: (line: string) => void;
  remedy?: string;
}

/** The inputs of verifyMainlandMirrors. */
export interface VerifyMainlandMirrorsOptions {
  baseUrl: string;
  version?: ProductVersion | null;
  platform?: string;
  latest?: boolean;
  fetchImpl?: FetchImpl;
  probeMirror?: ProbeMirror;
  log?: (line: string) => void;
}

/** What verifyMainlandMirrors checked: the version(s) and each result. */
export interface MirrorVerification {
  version: string;
  results: MirrorCheckResult[];
}

// One platform's mirror address to check; null when promoted without one.
type MirrorTarget =
  | { platform: string; url: string; fileName: string }
  | { platform: string; url: null; fileName: string };

// The Mainland Mirror is a manually uploaded share page of each installer at
// an address the operator supplies. This module proves that the page actually
// serves the installer the release ships.
//
// Lanzou answers HTTP 200 for present and absent shares alike. A present share
// names its file in <title>; an absent or cancelled one has an empty title and
// carries this marker. Mobile user agents get a page without those facts, so
// the probe identifies itself as a desktop browser.
const MISSING_MARKER = '文件取消分享';
const TITLE_SUFFIX = ' - 蓝奏云';
export const MIRROR_USER_AGENT =
  'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36';
const PAGE_TIMEOUT_MS = 20_000;
const PAGE_MAX_BYTES = 512 * 1024;

function decodeEntities(text: string): string {
  return text
    .replaceAll('&lt;', '<')
    .replaceAll('&gt;', '>')
    .replaceAll('&quot;', '"')
    .replaceAll('&#39;', "'")
    .replaceAll('&amp;', '&');
}

// Three outcomes: `verified` (the share names the expected installer),
// `missing-or-misnamed` (the operator can fix the share), `unverifiable` (the
// page could not be read or is not a share page this check understands).
export function classifyMirrorPage(
  page: MirrorPage,
  fileName: string
): MirrorClassification {
  if (page.error)
    return {
      outcome: 'unverifiable',
      detail: `request failed: ${page.error.message}`
    };
  if (page.status !== 200)
    return { outcome: 'unverifiable', detail: `HTTP ${page.status}` };
  const html = page.html ?? '';
  if (html.includes(MISSING_MARKER))
    return {
      outcome: 'missing-or-misnamed',
      detail: `share is missing or cancelled (${MISSING_MARKER})`
    };
  const match = /<title>([^<]*)<\/title>/i.exec(html);
  if (!match) return { outcome: 'unverifiable', detail: 'page has no <title>' };
  const title = decodeEntities(match[1]).trim();
  if (!title.endsWith(TITLE_SUFFIX))
    return {
      outcome: 'unverifiable',
      detail: `unexpected page title ${JSON.stringify(title)}`
    };
  const served = title.slice(0, -TITLE_SUFFIX.length).trim();
  if (served !== fileName)
    return {
      outcome: 'missing-or-misnamed',
      detail: `share serves ${served}, expected ${fileName}`
    };
  return { outcome: 'verified', detail: `share serves ${fileName}` };
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
    if (size > maxBytes) throw new Error('mirror page exceeds expected size');
    chunks.push(Buffer.from(chunk));
  }
  return Buffer.concat(chunks).toString('utf8');
}

export async function fetchMirrorPage(
  url: string,
  {
    fetchImpl = fetch,
    timeoutMs = PAGE_TIMEOUT_MS
  }: FetchMirrorPageOptions = {}
): Promise<MirrorPage> {
  try {
    const response = await fetchImpl(url, {
      headers: { 'user-agent': MIRROR_USER_AGENT, accept: 'text/html' },
      redirect: 'follow',
      signal: AbortSignal.timeout(timeoutMs)
    });
    return {
      status: response.status,
      html: await readBounded(response, PAGE_MAX_BYTES)
    };
  } catch (error) {
    return { error: error instanceof Error ? error : new Error(String(error)) };
  }
}

export async function checkMainlandMirror({
  platform,
  url,
  fileName,
  probeMirror
}: CheckMainlandMirrorOptions): Promise<MirrorCheckResult> {
  const page = await probeMirror(url);
  return { platform, url, fileName, ...classifyMirrorPage(page, fileName) };
}

export function describeMirrorResult(result: MirrorCheckResult): string {
  return `${result.platform}: ${result.outcome} ${result.url} (${result.detail})`;
}

const RECORD_REMEDY = 'fix the share or pass --allow-unverified-mirror';
export const VERIFY_REMEDY =
  'fix or re-upload the share and re-run mirror before promotion, or publish a new product version';

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

async function fetchJson(
  url: string,
  fetchImpl: FetchImpl,
  description: string,
  { missing }: { missing?: string } = {}
): Promise<unknown> {
  const response = await fetchImpl(url, {
    headers: { accept: 'application/json' },
    signal: AbortSignal.timeout(PAGE_TIMEOUT_MS)
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
    return JSON.parse(await readBounded(response, PAGE_MAX_BYTES));
  } catch {
    throw new Error(`Invalid JSON in ${description}`);
  }
}

// Only Lockstep Platforms record a mainland mirror; Android has none yet
// (ADR 0012).
function platformKeysFor(
  platform: string | undefined
): readonly ReleasePlatformKey[] {
  if (!platform) return LOCKSTEP_PLATFORM_KEYS;
  const { key } = releasePlatform(platform);
  if (!(LOCKSTEP_PLATFORM_KEYS as readonly ReleasePlatformKey[]).includes(key))
    throw new Error(`${platform} has no mainland mirror`);
  return [key];
}

// Read-only check against the public release origin; needs no credentials.
// Before promotion it re-checks the mirror records uploaded for `version`;
// with `latest` it checks each published Platform Release Manifest instead,
// so the check can be repeated for a release after VERSION has moved on.
export async function verifyMainlandMirrors({
  baseUrl,
  version = null,
  platform = undefined,
  latest = false,
  fetchImpl = fetch,
  probeMirror = (url) => fetchMirrorPage(url, { fetchImpl }),
  log = () => {}
}: VerifyMainlandMirrorsOptions): Promise<MirrorVerification> {
  const origin = baseUrl.replace(/\/$/, '');
  const targets: MirrorTarget[] = [];
  let target = version;
  if (latest) {
    const versions: ProductVersion[] = [];
    for (const key of platformKeysFor(platform)) {
      const manifestPath = platformManifestPath(key);
      // A Lockstep Platform's manifest is a desktop one; the check proves it.
      const manifest = validatePlatformManifest(
        (await fetchJson(`${origin}/${manifestPath}`, fetchImpl, manifestPath, {
          missing: `${key} has no platform manifest yet at ${manifestPath}`
        })) as DesktopPlatformManifest,
        key
      );
      versions.push(manifest.version);
      const download = manifest.downloads[key];
      targets.push({
        platform: key,
        url: download.mainlandUrl ?? null,
        fileName: installerFileName(download.url)
      });
    }
    target = [...new Set(versions)].join(' / ');
  } else {
    // assertProductVersion returns the version it accepts.
    target = assertProductVersion(target);
    for (const key of platformKeysFor(platform)) {
      const uploadedKey = fragmentKey(target, key);
      // A Lockstep Platform's fragment is a desktop one; the check proves it.
      const fragment = validatePlatformFragment(
        (await fetchJson(`${origin}/${uploadedKey}`, fetchImpl, uploadedKey, {
          missing: `${key} ${target} is not uploaded yet; run upload first`
        })) as DesktopPlatformFragment,
        target,
        key
      );
      const fileName = installerFileName(fragment.installer.url);
      const recordKey = mirrorRecordKey(target, key);
      const record = validateMirrorRecord(
        (await fetchJson(`${origin}/${recordKey}`, fetchImpl, recordKey, {
          missing: `${key} ${target} has no mainland mirror recorded; run mirror first`
        })) as MirrorRecord,
        target,
        key,
        fileName
      );
      targets.push({ platform: key, url: record.url, fileName });
    }
  }
  const results: MirrorCheckResult[] = [];
  for (const entry of targets)
    results.push(
      entry.url
        ? await checkMainlandMirror({ ...entry, probeMirror })
        : {
            ...entry,
            outcome: 'waived',
            detail: 'promoted without a mainland mirror'
          }
    );
  for (const result of results) log(describeMirrorResult(result));
  assertMainlandMirrors(results, { remedy: VERIFY_REMEDY });
  return { version: target, results };
}
