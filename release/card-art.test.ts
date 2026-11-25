import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { expect, test } from 'vite-plus/test';
import { cardArtStale, publishCardArt } from './card-art.ts';
import type { StoredObject } from './r2-store.ts';

function memoryStore() {
  const objects = new Map<string, StoredObject>();
  const order: string[] = [];
  return {
    objects,
    order,
    async get(key: string) {
      return objects.get(key) ?? null;
    },
    async put(key: string, bytes: Buffer) {
      order.push(key);
      objects.set(key, { bytes, etag: `"${objects.size}"` });
      return true;
    }
  };
}

const text = {
  en: 'text-en-' + 'a'.repeat(20) + '.json',
  zh: 'text-zh-' + 'b'.repeat(20) + '.json'
};

function artDirectory(files: string[]) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-card-art-'));
  for (const file of [...files, ...Object.values(text)])
    fs.writeFileSync(path.join(dir, file), file);
  fs.writeFileSync(
    path.join(dir, 'index.json'),
    JSON.stringify({
      version: 2,
      text,
      game: { buildid: '7', gameData: 'e1', zh: 'e1' },
      cards: {},
      names: {}
    })
  );
  return dir;
}

test('a publish writes each art file, then the index last', async () => {
  const art = 'a'.repeat(20) + '.webp';
  const store = memoryStore();
  await expect(publishCardArt(artDirectory([art]), store)).resolves.toBe(3);
  expect(store.order).toEqual([art, text.en, text.zh, 'index.json']);
});

test('a publish refuses a file that is not a content digest', async () => {
  await expect(
    publishCardArt(artDirectory(['cover.webp']), memoryStore())
  ).rejects.toThrow('unexpected Card Art file');
});

test('the art is stale until the index has both the buildid and the GameData', async () => {
  const index = path.join(artDirectory([]), 'index.json');
  const runTool = () => ({ buildid: '7' });
  const stale = (file: string, etag: string) =>
    cardArtStale(file, runTool, async () => etag);
  await expect(stale(index, 'e1')).resolves.toMatchObject({ stale: false });
  await expect(stale(index, 'e2')).resolves.toMatchObject({ stale: true });
  await expect(stale(`${index}.missing`, 'e1')).resolves.toMatchObject({
    stale: true
  });
});

test('an index without card names is stale, so the next run publishes them', async () => {
  const index = path.join(artDirectory([]), 'index.json');
  fs.writeFileSync(
    index,
    JSON.stringify({ version: 1, game: { buildid: '7', gameData: 'e1' } })
  );
  await expect(
    cardArtStale(
      index,
      () => ({ buildid: '7' }),
      async () => 'e1'
    )
  ).resolves.toMatchObject({ stale: true });
});

test('a changed translation or missing locale triggers a backfill', async () => {
  const index = path.join(artDirectory([]), 'index.json');
  const run = () => ({ buildid: '7' });
  expect(
    (
      await cardArtStale(index, run, async (url) =>
        url.endsWith('.bytes') ? 'new' : 'e1'
      )
    ).stale
  ).toBe(true);
  const previous = JSON.parse(fs.readFileSync(index, 'utf8'));
  delete previous.text.zh;
  fs.writeFileSync(index, JSON.stringify(previous));
  expect((await cardArtStale(index, run, async () => 'e1')).stale).toBe(true);
});
