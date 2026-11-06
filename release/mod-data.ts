import fs from 'node:fs';
import {
  RELEASE_BUCKET,
  putReplaceable,
  r2StoreFromEnvironment
} from './r2-store.ts';
import type { ObjectStore } from './r2-store.ts';

/** A name `just release::mod-data` accepts. */
export type ModDataName = 'supporters' | 'voice-lines';

/** Where one mod data file lives under mod-data/, and its smallest size. */
export interface ModDataEntry {
  key: string;
  minBytes: number;
}

/** The options of uploadModData; the upload reads and writes only through get and put. */
export interface UploadModDataOptions {
  store?: Pick<ObjectStore, 'get' | 'put'>;
}

// Data the mod refreshes at runtime from mod-data/ in the public bucket,
// https://cdn.bazaarplusplus.com/mod-data/<key>. The mod embeds a copy at build
// time (bazaarplusplus-mod/src/BazaarPlusPlus/RemoteEmbeddedData.targets), so
// the minimum sizes match what that build accepts.
export const MOD_DATA_PREFIX = 'mod-data';
export const MOD_DATA: Readonly<Record<ModDataName, ModDataEntry>> =
  Object.freeze({
    supporters: { key: 'supporters/supporter-list.json', minBytes: 2 },
    'voice-lines': { key: 'voice-lines/voice-lines.json', minBytes: 102400 }
  });

export function modDataObject(name: string, bytes: Buffer): string {
  // The name comes from the command line, so it may name no entry.
  const entries: Readonly<Partial<Record<string, ModDataEntry>>> = MOD_DATA;
  const entry = entries[name];
  if (!entry)
    throw new Error(
      `mod data must be one of ${Object.keys(MOD_DATA).join(', ')}`
    );
  if (bytes.length < entry.minBytes)
    throw new Error(`${name} is smaller than ${entry.minBytes} bytes`);
  JSON.parse(bytes.toString('utf8'));
  return entry.key;
}

export async function uploadModData(
  name: string,
  file: string,
  { store }: UploadModDataOptions = {}
): Promise<string> {
  const bytes = fs.readFileSync(file);
  const key = modDataObject(name, bytes);
  store ??= r2StoreFromEnvironment(process.env, {
    bucket: RELEASE_BUCKET,
    prefix: MOD_DATA_PREFIX
  });
  await putReplaceable(store, key, bytes, 'application/json');
  return `https://cdn.bazaarplusplus.com/${MOD_DATA_PREFIX}/${key}`;
}

if (import.meta.main) {
  const [name, file] = process.argv.slice(2);
  try {
    if (!name || !file)
      throw new Error(
        'usage: node release/mod-data.ts <supporters|voice-lines> <file>'
      );
    process.stdout.write(`${await uploadModData(name, file)}\n`);
  } catch (error) {
    process.stderr.write(`${error instanceof Error ? error.message : error}\n`);
    process.exitCode = 2;
  }
}
