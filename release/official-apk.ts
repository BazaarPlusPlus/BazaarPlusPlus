import fs from 'node:fs';
import { sha256File } from './android-apk.ts';
import { gameLibsStoreFromEnvironment } from './game-assembly-snapshot.ts';
import { androidInputKey, lockedInteropEntry } from './interop-snapshot.ts';
import { WORKSPACE_ROOT } from './product.ts';
import type { ObjectStore } from './r2-store.ts';
import {
  PUBLISHER_KEY,
  isApkEtag,
  writeGameLibsLockEntry
} from './snapshot-lock.ts';
import type { InteropLockEntry } from './snapshot-lock.ts';

// The official Android APK watch (root GLOSSARY.md, Interop Snapshot): the
// publisher's APK address, whether it differs from the APK the android-publisher
// entry locks, what to do with a downloaded copy, and recording the ETag it is
// now served with. .github/workflows/android-apk-watch.yml drives it through
// the official-apk, downloaded-apk and record-apk-etag verbs of game-libs.ts
// and imports OFFICIAL_ANDROID_APK_URL with bun -e.

// The value of a strong ETag header ("abc" -> abc); null for a weak, missing or
// malformed one, which identifies no bytes.
export function strongEtag(header: string | null | undefined): string | null {
  const match = /^"([\x21\x23-\x7e]{1,256})"$/.exec(header?.trim() ?? '');
  return match ? match[1] : null;
}

// Where the publisher serves the official Android APK. It overwrites the file in
// place on every game update, which is why the private bucket keeps each one.
export const OFFICIAL_ANDROID_APK_URL =
  'https://data.playthebazaar.com/game/android/TheBazaar.apk';

// Whether the official APK differs from the one the android-publisher entry
// locks, read without downloading either: the publisher's Content-Length
// against the stored copy's size, and its ETag against the one the entry
// recorded. An entry without an ETag (a manual capture) compares sizes only.
/** The HEAD request officialApkChanged sends: `fetch`, or a test double. */
export type ApkHeadFetch = (
  url: string,
  init: RequestInit
) => Promise<Pick<Response, 'ok' | 'status' | 'headers'>>;

export interface OfficialApkChangedOptions {
  workspaceRoot?: string;
  url?: string;
  storeFactory?: () => Pick<ObjectStore, 'head'>;
  fetchImpl?: ApkHeadFetch;
}

/** The publisher's APK against the one the android-publisher entry locks. */
export interface OfficialApkCheck {
  changed: boolean;
  officialSize: number;
  officialEtag: string | null;
  lockedSize: number;
  lockedEtag: string | null;
  lockedGameVersion: string;
}

export async function officialApkChanged({
  workspaceRoot = WORKSPACE_ROOT,
  url = OFFICIAL_ANDROID_APK_URL,
  storeFactory = gameLibsStoreFromEnvironment,
  fetchImpl = fetch
}: OfficialApkChangedOptions): Promise<OfficialApkCheck> {
  const response = await fetchImpl(url, { method: 'HEAD', redirect: 'follow' });
  if (!response.ok) throw new Error(`HEAD ${url} answered ${response.status}`);
  const officialSize = Number(response.headers.get('content-length'));
  if (!Number.isSafeInteger(officialSize) || officialSize <= 0)
    throw new Error(`HEAD ${url} gave no Content-Length`);
  const officialEtag = strongEtag(response.headers.get('etag'));
  const entry = lockedInteropEntry(workspaceRoot, 'publisher');
  const key = androidInputKey('apk', entry.apkSha256);
  const stored = await storeFactory().head(key);
  if (!stored) throw new Error(`The locked APK ${key} is not in the store`);
  return {
    changed:
      stored.size !== officialSize ||
      (entry.apkEtag !== null && officialEtag !== entry.apkEtag),
    officialSize,
    officialEtag,
    lockedSize: stored.size,
    lockedEtag: entry.apkEtag,
    lockedGameVersion: entry.gameVersion
  };
}

// What the watch does with the official APK it downloaded: `capture` when its
// bytes are not the locked ones, `etag` when they are but the publisher now
// serves them under another strong ETag (record it, or every check downloads
// again), `none` otherwise. A download without a strong ETag never asks for a
// record: there is nothing to write.
export type DownloadedApkKind = 'capture' | 'etag' | 'none';

export function downloadedApkKind(
  entry: Pick<InteropLockEntry, 'apkSha256' | 'apkEtag'>,
  sha256: string,
  etag: string | null
): DownloadedApkKind {
  if (sha256 !== entry.apkSha256) return 'capture';
  return etag !== null && etag !== entry.apkEtag ? 'etag' : 'none';
}

// The strong ETag in the response headers `curl --dump-header` wrote. A
// redirect leaves one header block per response, so the last ETag is the one
// the bytes came with.
export function etagOfHeaders(headers: string): string | null {
  const line = headers
    .split(/\r?\n/)
    .filter((header) => /^etag:/i.test(header))
    .at(-1);
  return strongEtag(line?.slice('etag:'.length));
}

/** A downloaded official APK against the android-publisher entry. */
export interface DownloadedApk {
  kind: DownloadedApkKind;
  size: number;
  sha256: string;
  etag: string | null;
  lockedSha256: string;
  lockedEtag: string | null;
  /** The branch of the pull request that records the ETag; null unless `etag`. */
  etagBranch: string | null;
}

export interface InspectDownloadedApkOptions {
  workspaceRoot?: string;
  apk: string;
  /** The file `curl --dump-header` wrote for the download. */
  headers: string;
}

export async function inspectDownloadedApk({
  workspaceRoot = WORKSPACE_ROOT,
  apk,
  headers
}: InspectDownloadedApkOptions): Promise<DownloadedApk> {
  const entry = lockedInteropEntry(workspaceRoot, 'publisher');
  const sha256 = await sha256File(apk);
  const etag = etagOfHeaders(fs.readFileSync(headers, 'utf8'));
  const kind = downloadedApkKind(entry, sha256, etag);
  return {
    kind,
    size: fs.statSync(apk).size,
    sha256,
    etag,
    lockedSha256: entry.apkSha256,
    lockedEtag: entry.apkEtag,
    etagBranch:
      kind === 'etag' && etag !== null
        ? `chore/android-apk-etag-${entry.versionCode}-${etag.slice(0, 8)}`
        : null
  };
}

// Records the ETag the publisher now serves the locked APK with, after
// checking that `apk` is that APK: the publisher can change the ETag of
// unchanged bytes, and an entry that kept the old one would be downloaded on
// every check.
export interface RecordApkEtagOptions {
  workspaceRoot?: string;
  apk: string;
  etag: string;
}

export async function recordApkEtag({
  workspaceRoot = WORKSPACE_ROOT,
  apk,
  etag
}: RecordApkEtagOptions): Promise<string> {
  if (!isApkEtag(etag)) throw new Error(`Not a strong ETag value: ${etag}`);
  const entry = lockedInteropEntry(workspaceRoot, 'publisher');
  const sha256 = await sha256File(apk);
  if (sha256 !== entry.apkSha256)
    throw new Error(
      `${apk} is ${sha256}, not the locked APK ${entry.apkSha256}; capture it with snapshot-android`
    );
  writeGameLibsLockEntry(workspaceRoot, PUBLISHER_KEY, {
    ...entry,
    apkEtag: etag
  });
  return entry.gameVersion;
}
