import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseArgs } from 'node:util';
import { INTEROP_KEY, lockedInteropEntry } from './game-libs.ts';
import type { InteropLockEntry } from './game-libs.ts';
import { WORKSPACE_ROOT } from './product.ts';

// How android-apk-watch.yml sorts a capture of a new official APK
// (docs/release.md "Android 发布顺序"): `content` when the game code is the
// locked one, so the lock alone changes; `code` when it changed, so the next
// Product Release adapts the Android App to it.

// How a captured android-online entry differs from the previous one:
// `unchanged` for the same APK, `content` when the APK changed but the game
// code (libil2cpp.so and global-metadata.dat) did not, `code` otherwise. The
// Android App judges by the game code alone, so a content-only update needs
// no new adaptation and no release. The same game code must give the same
// Interop Snapshot; a different one means the capture is not reproducible,
// and nobody should ship it unreviewed.
export type CaptureKind = 'unchanged' | 'content' | 'code';

export function captureKind(
  previous: InteropLockEntry | null | undefined,
  next: InteropLockEntry | null | undefined
): CaptureKind {
  if (!next) throw new Error(`No captured ${INTEROP_KEY} entry to compare`);
  if (!previous) return 'code';
  if (previous.apkSha256 === next.apkSha256) return 'unchanged';
  if (
    previous.il2cppSha256 !== next.il2cppSha256 ||
    previous.metadataSha256 !== next.metadataSha256
  )
    return 'code';
  if (previous.interopSha256 !== next.interopSha256)
    throw new Error(
      `The new APK has the locked game code but its Interop Snapshot hashes to ${next.interopSha256}, not the locked ${previous.interopSha256}; check the capture toolchain before recording it`
    );
  return 'content';
}

const usage = `Usage:
  bun release/android-adaptation.ts kind --previous <game-libs.lock.json>
      Print unchanged, content or code: how the locked android-online entry
      differs from the one in the previous lock file`;

// The previous lock file, read unvalidated: only its android-online entry counts.
interface PreviousLock {
  entries?: { [INTEROP_KEY]?: InteropLockEntry | null };
}

export interface CliOptions {
  workspaceRoot?: string;
}

export async function cliMain(
  argv: readonly string[],
  { workspaceRoot = WORKSPACE_ROOT }: CliOptions = {}
): Promise<CaptureKind> {
  const [command, ...rest] = argv;
  const { values } = parseArgs({
    args: rest,
    strict: true,
    options: { previous: { type: 'string' } }
  });
  if (command === 'kind' && values.previous) {
    const previous = (
      JSON.parse(fs.readFileSync(values.previous, 'utf8')) as PreviousLock
    ).entries?.[INTEROP_KEY];
    return captureKind(previous, lockedInteropEntry(workspaceRoot));
  }
  throw new Error(usage);
}

if (
  process.argv[1] &&
  path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)
)
  cliMain(process.argv.slice(2)).then(
    (line) => console.log(line),
    (error: Error) => {
      console.error(error.message);
      process.exitCode = 1;
    }
  );
