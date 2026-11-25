import { createHash } from 'node:crypto';
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
  const digest = (data: string) =>
    createHash('sha256').update(data).digest('hex').slice(0, 20);
  const art = digest('art') + '.webp';
  const texture = 'tex-' + digest('mask') + '.png';
  const descriptor = JSON.stringify({
    version: 1,
    textures: { _MainTex: { file: art }, _EnchantmentMask: { file: texture } }
  });
  const material = 'fx-' + digest(descriptor) + '.json';
  for (const [name, bytes] of [
    [art, 'art'],
    [texture, 'mask'],
    [material, descriptor]
  ])
    fs.writeFileSync(path.join(dir, name), bytes);
  for (const file of [...files, ...Object.values(text)])
    fs.writeFileSync(path.join(dir, file), file);
  fs.writeFileSync(
    path.join(dir, 'index.json'),
    JSON.stringify({
      version: 2,
      text,
      game: { buildid: '7', gameData: 'e1', zh: 'e1' },
      cards: { example: art },
      fx: { example: material },
      names: {}
    })
  );
  return dir;
}

test('a publish writes each art file, then the index last', async () => {
  const store = memoryStore();
  await expect(publishCardArt(artDirectory([]), store)).resolves.toBe(5);
  expect(store.order.at(-1)).toBe('index.json');
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

test('a missing packed texture prevents index replacement', async () => {
  const dir = artDirectory([]);
  fs.unlinkSync(
    path.join(
      dir,
      fs.readdirSync(dir).find((name) => name.endsWith('.png'))!
    )
  );
  const store = memoryStore();
  await expect(publishCardArt(dir, store)).rejects.toThrow(
    'missing or corrupt'
  );
  expect(store.order).not.toContain('index.json');
});

test('the published v2 index before Enchantment FX needs backfill', async () => {
  const index = path.join(artDirectory([]), 'index.json');
  const previous = JSON.parse(fs.readFileSync(index, 'utf8'));
  delete previous.fx;
  fs.writeFileSync(index, JSON.stringify(previous));
  expect(
    (
      await cardArtStale(
        index,
        () => ({ buildid: '7' }),
        async () => 'e1'
      )
    ).stale
  ).toBe(true);
});

test('retired material and packed textures can resolve from an earlier immutable publish', async () => {
  const store = memoryStore();
  const dir = artDirectory([]);
  await publishCardArt(dir, store);
  for (const file of fs
    .readdirSync(dir)
    .filter((name) => name.startsWith('tex-') || name.startsWith('fx-')))
    fs.unlinkSync(path.join(dir, file));
  store.order.length = 0;
  await publishCardArt(dir, store);
  expect(
    store.order.some(
      (file) => file.startsWith('tex-') || file.startsWith('fx-')
    )
  ).toBe(false);
});
