import type { InteropLockEntry } from './snapshot-lock.ts';

// The Android App compares game code, so content-only updates need no release.
// Different Interop Snapshots for identical game code require review.
export type CaptureKind = 'unchanged' | 'content' | 'code';

export function captureKind(
  previous: InteropLockEntry | null | undefined,
  next: InteropLockEntry | null | undefined
): CaptureKind {
  if (!next) throw new Error('No captured Android entry to compare');
  if (!previous) return 'code';
  // A recapture of the locked APKs regenerates the interop, and
  // interop-manifest.json records when (ADR 0008): its digest differs.
  if (
    [previous.apkSha256, ...previous.splitApkSha256s].join() ===
    [next.apkSha256, ...next.splitApkSha256s].join()
  )
    return 'unchanged';
  if (
    previous.il2cppSha256 !== next.il2cppSha256 ||
    previous.metadataSha256 !== next.metadataSha256
  )
    return 'code';
  if (previous.interopSha256 !== next.interopSha256)
    throw new Error(
      `The captured build has the locked game code but its Interop Snapshot hashes to ${next.interopSha256}, not the locked ${previous.interopSha256}; check the capture toolchain before recording it`
    );
  return 'content';
}
