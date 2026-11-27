import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { DatabaseSync } from 'node:sqlite';
import { test } from 'vite-plus/test';
import { defaultCacheDir, extractZhNames } from './zh-names.ts';

const md5 = (text: string): string =>
  crypto.createHash('md5').update(text).digest('hex');

test('defaultCacheDir resolves platform-specific game cache locations', () => {
  assert.equal(
    defaultCacheDir('darwin', '/Users/alice'),
    '/Users/alice/Library/Application Support/com.TempoStorm.TheBazaar/prod/cache'
  );
  assert.equal(
    defaultCacheDir('win32', 'C:\\Users\\alice', {
      USERPROFILE: 'C:\\Users\\alice'
    }),
    'C:\\Users\\alice\\AppData\\LocalLow\\Tempo Storm\\The Bazaar\\prod\\cache'
  );
  assert.equal(
    defaultCacheDir('linux', '/home/alice'),
    '/home/alice/.config/unity3d/Tempo Storm/The Bazaar/prod/cache'
  );
});

test('extractZhNames joins GameData cards and enchantments with zh-CN translations', (t) => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-zh-names-'));
  t.onTestFinished(() => fs.rmSync(dir, { recursive: true, force: true }));
  fs.mkdirSync(path.join(dir, 'translations'), { recursive: true });

  const translations = new DatabaseSync(
    path.join(dir, 'translations', 'zh-CN.bytes')
  );
  translations.exec(
    'CREATE TABLE translation (hash TEXT PRIMARY KEY, text TEXT NOT NULL)'
  );
  const putTranslation = translations.prepare(
    'INSERT INTO translation (hash, text) VALUES (?, ?)'
  );
  for (const [en, zh] of [
    ['Dooley', '杜利'],
    ['Jules', '朱尔斯'],
    ['Karnok', '卡诺克'],
    ['Mak', '马克'],
    ['Pygmalien', '皮格马利翁'],
    ['Stelle', '斯特拉'],
    ['The Dragons', '巨龙'],
    ['Vanessa', '瓦妮莎'],
    ['Cutlass', '弯刀'],
    ['Fiery', '炽热']
  ]) {
    putTranslation.run(md5(en), zh);
  }
  translations.close();

  const game = new DatabaseSync(path.join(dir, 'GameData.db'));
  game.exec('CREATE TABLE cards (Id TEXT PRIMARY KEY, Data TEXT NOT NULL)');
  game.prepare('INSERT INTO cards (Id, Data) VALUES (?, ?)').run(
    'card-1',
    JSON.stringify({
      $type: 'TCardItem',
      Localization: { Title: { Text: 'Cutlass' } },
      Enchantments: { Fiery: {} }
    })
  );
  game.close();

  const { document, englishCount } = extractZhNames(dir);
  assert.equal(englishCount, 2);
  assert.equal(document.heroes.Vanessa, '瓦妮莎');
  assert.equal(document.heroes.TheDragons, '巨龙');
  assert.deepEqual(document.cards, { 'card-1': 'Cutlass' });
  assert.deepEqual(document.names, { Cutlass: '弯刀', Fiery: '炽热' });
});
