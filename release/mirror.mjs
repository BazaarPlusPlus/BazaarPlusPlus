import {
  installerFileName,
  mirrorRecordKey,
  platformManifestPath,
  validateMirrorRecord,
  validatePlatformFragment,
  validatePlatformManifest
} from './manifest.mjs';
import { assertProductVersion } from './product.mjs';
import {
  RELEASE_PLATFORMS,
  RELEASE_PLATFORM_KEYS
} from './release-platforms.mjs';

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

function decodeEntities(text) {
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
export function classifyMirrorPage(page, fileName) {
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

async function readBounded(response, maxBytes) {
  if (!response.body) return '';
  const chunks = [];
  let size = 0;
  for await (const chunk of response.body) {
    size += chunk.length;
    if (size > maxBytes) throw new Error('mirror page exceeds expected size');
    chunks.push(Buffer.from(chunk));
  }
  return Buffer.concat(chunks).toString('utf8');
}

export async function fetchMirrorPage(
  url,
  { fetchImpl = fetch, timeoutMs = PAGE_TIMEOUT_MS } = {}
) {
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
}) {
  const page = await probeMirror(url);
  return { platform, url, fileName, ...classifyMirrorPage(page, fileName) };
}

export function describeMirrorResult(result) {
  return `${result.platform}: ${result.outcome} ${result.url} (${result.detail})`;
}

const RECORD_REMEDY = 'fix the share or pass --allow-unverified-mirror';
export const VERIFY_REMEDY =
  'fix or re-upload the share and re-run mirror before promotion, or publish a new product version';

export function assertMainlandMirrors(
  results,
  { allowUnverified = false, log = () => {}, remedy = RECORD_REMEDY } = {}
) {
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

async function fetchJson(url, fetchImpl, description, { missing } = {}) {
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

function platformKeysFor(platform) {
  if (!platform) return RELEASE_PLATFORM_KEYS;
  const definition = RELEASE_PLATFORMS.find(
    (candidate) => candidate.buildPlatform === platform
  );
  if (!definition) throw new Error(`Unsupported release platform: ${platform}`);
  return [definition.key];
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
}) {
  const origin = baseUrl.replace(/\/$/, '');
  const targets = [];
  let target = version;
  if (latest) {
    const versions = [];
    for (const key of platformKeysFor(platform)) {
      const manifestPath = platformManifestPath(key);
      const manifest = validatePlatformManifest(
        await fetchJson(`${origin}/${manifestPath}`, fetchImpl, manifestPath, {
          missing: `${key} has no platform manifest yet; it predates per-platform promotion`
        }),
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
    assertProductVersion(target);
    for (const key of platformKeysFor(platform)) {
      const fragmentKey = `${target}/${key}/updater/platform-manifest.json`;
      const fragment = validatePlatformFragment(
        await fetchJson(`${origin}/${fragmentKey}`, fetchImpl, fragmentKey, {
          missing: `${key} ${target} is not uploaded yet; run upload first`
        }),
        target,
        key
      );
      const fileName = installerFileName(fragment.installer.url);
      const recordKey = mirrorRecordKey(target, key);
      const record = validateMirrorRecord(
        await fetchJson(`${origin}/${recordKey}`, fetchImpl, recordKey, {
          missing: `${key} ${target} has no mainland mirror recorded; run mirror first`
        }),
        target,
        key,
        fileName
      );
      targets.push({ platform: key, url: record.url, fileName });
    }
  }
  const results = [];
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
