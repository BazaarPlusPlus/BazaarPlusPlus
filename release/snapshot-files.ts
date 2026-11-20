import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';

export const hash = (bytes: string | NodeJS.ArrayBufferView): string =>
  crypto.createHash('sha256').update(bytes).digest('hex');
export const jsonBytes = (value: unknown): Buffer =>
  Buffer.from(`${JSON.stringify(value, null, 2)}\n`);

export const isSha256 = (value: unknown): boolean =>
  /^[0-9a-f]{64}$/.test(String(value ?? ''));

export function readSnapshotManifest<Manifest>(
  directory: string
): Manifest | null {
  const file = path.join(directory, 'manifest.json');
  if (!fs.existsSync(file)) return null;
  return JSON.parse(fs.readFileSync(file, 'utf8')) as Manifest;
}

export type ExecFileError = Error & { stderr?: Buffer | string };

// COPYFILE_DISABLE keeps macOS tar from adding AppleDouble files that change the extracted digest on Linux.
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
