import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { expect, test } from 'vitest';
import { modDataObject, uploadModData } from './mod-data.mjs';

function memoryStore() {
  const objects = new Map();
  return {
    objects,
    async get(key) {
      return objects.get(key) ?? null;
    },
    async put(key, bytes) {
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
    'mod data must be one of supporters, voice-lines'
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
    store.objects.get('supporters/supporter-list.json').bytes.toString()
  ).toBe('[{"name":"x"}]');
});
