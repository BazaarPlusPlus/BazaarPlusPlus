import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';

// The file helpers that the Snapshot Lock, Game Assembly Snapshot and Interop
// Snapshot code share (root GLOSSARY.md): sha256, the two-space JSON bytes
// every snapshot file and the lock are written in, a snapshot directory's
// manifest.json, and tar. It imports no other snapshot module.

export const hash = (bytes: string | NodeJS.ArrayBufferView): string =>
  crypto.createHash('sha256').update(bytes).digest('hex');
export const jsonBytes = (value: unknown): Buffer =>
  Buffer.from(`${JSON.stringify(value, null, 2)}\n`);

// String() is the conversion RegExp.prototype.test applies to its argument.
export const isSha256 = (value: unknown): boolean =>
  /^[0-9a-f]{64}$/.test(String(value ?? ''));

// A snapshot directory's manifest.json, or null when it has none. The caller
// names the manifest type: Managed or Interop.
export function readSnapshotManifest<Manifest>(
  directory: string
): Manifest | null {
  const file = path.join(directory, 'manifest.json');
  if (!fs.existsSync(file)) return null;
  return JSON.parse(fs.readFileSync(file, 'utf8')) as Manifest;
}

// execFileSync fails with the child's stderr attached.
export type ExecFileError = Error & { stderr?: Buffer | string };

// macOS tar otherwise packs every extended attribute as an AppleDouble
// `._<name>` member that GNU tar extracts as a real file, which changes the
// directory digest on Linux. COPYFILE_DISABLE turns that off; GNU tar ignores
// it.
export function tar(args: readonly string[], cwd: string): void {
  try {
    execFileSync('tar', args, {
      cwd,
      stdio: ['ignore', 'ignore', 'pipe'],
      env: { ...process.env, COPYFILE_DISABLE: '1' }
    });
  } catch (error) {
    const failure = error as ExecFileError;
    throw new Error(
      `tar ${args[0]} failed: ${String(failure.stderr ?? failure.message).trim()}`
    );
  }
}
