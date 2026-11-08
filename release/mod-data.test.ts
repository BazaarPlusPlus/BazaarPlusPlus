import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { expect, test } from 'vitest';
import { modDataObject, uploadModData } from './mod-data.ts';
import type { StoredObject } from './r2-store.ts';

function memoryStore() {
  const objects = new Map<string, StoredObject>();
  return {
    objects,
    async get(key: string) {
      return objects.get(key) ?? null;
    },
    async put(key: string, bytes: Buffer) {
      objects.set(key, { bytes, etag: `"${objects.size}"` });
      return true;
    }
  };
}

test('mod data names map to fixed keys and are checked before upload', () => {
  expect(modDataObject('supporters', Buffer.from('[]'))).toBe(
    'supporters/supporter-list.json'
  );
  expect(() => modDataObject('builds', Buffer.from('[]'))).toThrow(
    'mod data must be one of supporters, voice-lines, zh-names'
  );
  expect(() => modDataObject('voice-lines', Buffer.from('[]'))).toThrow(
    'smaller than 102400 bytes'
  );
  expect(() => modDataObject('supporters', Buffer.from('{oops'))).toThrow();
});

test('an upload writes the file under its key and returns its public URL', async () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-mod-data-'));
  const file = path.join(dir, 'supporter-list.json');
  fs.writeFileSync(file, '[{"name":"x"}]');
  const store = memoryStore();
  await expect(uploadModData('supporters', file, { store })).resolves.toBe(
    'https://cdn.bazaarplusplus.com/mod-data/supporters/supporter-list.json'
  );
  expect(
    store.objects.get('supporters/supporter-list.json')!.bytes.toString()
  ).toBe('[{"name":"x"}]');
});
