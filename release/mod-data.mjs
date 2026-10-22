import fs from 'node:fs';
import {
  RELEASE_BUCKET,
  putReplaceable,
  r2StoreFromEnvironment
} from './r2-store.mjs';

// Data the mod refreshes at runtime from mod-data/ in the public bucket,
// https://cdn.bazaarplusplus.com/mod-data/<key>. The mod embeds a copy at build
// time (bazaarplusplus-mod/src/BazaarPlusPlus/RemoteEmbeddedData.targets), so
// the minimum sizes match what that build accepts.
export const MOD_DATA_PREFIX = 'mod-data';
export const MOD_DATA = Object.freeze({
  supporters: { key: 'supporters/supporter-list.json', minBytes: 2 },
  'voice-lines': { key: 'voice-lines/voice-lines.json', minBytes: 102400 }
});

export function modDataObject(name, bytes) {
  const entry = MOD_DATA[name];
  if (!entry)
    throw new Error(
      `mod data must be one of ${Object.keys(MOD_DATA).join(', ')}`
    );
  if (bytes.length < entry.minBytes)
    throw new Error(`${name} is smaller than ${entry.minBytes} bytes`);
  JSON.parse(bytes.toString('utf8'));
  return entry.key;
}

export async function uploadModData(name, file, { store } = {}) {
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
        'usage: node release/mod-data.mjs <supporters|voice-lines> <file>'
      );
    process.stdout.write(`${await uploadModData(name, file)}\n`);
  } catch (error) {
    process.stderr.write(`${error instanceof Error ? error.message : error}\n`);
    process.exitCode = 2;
  }
}
