// The translation table indexes Chinese strings by the MD5 of their English text.
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import crypto from 'node:crypto';
import { DatabaseSync } from 'node:sqlite';

const HEROES: Record<string, string> = {
  Dooley: 'Dooley',
  Jules: 'Jules',
  Karnok: 'Karnok',
  Mak: 'Mak',
  Pygmalien: 'Pygmalien',
  Stelle: 'Stelle',
  TheDragons: 'The Dragons',
  Vanessa: 'Vanessa'
};

const [target, cacheArg] = process.argv.slice(2);
if (!target)
  throw new Error(
    'usage: bun scripts/qqbot/zh-names.ts <out.json> [cache dir]'
  );
const cache =
  cacheArg ??
  path.join(
    os.homedir(),
    'Library/Application Support/com.TempoStorm.TheBazaar/prod/cache'
  );
const open = (file: string) =>
  new DatabaseSync(path.join(cache, file), { readOnly: true });

const translations = open('translations/zh-CN.bytes');
const lookup = translations.prepare(
  'SELECT text FROM translation WHERE hash = ?'
);
const zh = (english: string) => {
  const hash = crypto.createHash('md5').update(english).digest('hex');
  return (lookup.get(hash) as { text: string } | undefined)?.text;
};

const game = open('GameData.db');
const english = new Set<string>();
const cards: Record<string, string> = {};
for (const { id, name } of game
  .prepare(
    `SELECT Id AS id, json_extract(Data, '$.Localization.Title.Text') AS name FROM cards
     WHERE json_extract(Data, '$."$type"') IN ('TCardItem', 'TCardSkill')`
  )
  .all() as { id: string; name: string | null }[]) {
  if (name) {
    english.add(name);
    cards[id] = name;
  }
}
for (const { name } of game
  .prepare(
    `SELECT DISTINCT e.key AS name FROM cards, json_each(cards.Data, '$.Enchantments') AS e
     WHERE e.type = 'object'`
  )
  .all() as { name: string | null }[])
  if (name) english.add(name);

const names: Record<string, string> = {};
for (const name of [...english].sort()) {
  const text = zh(name);
  if (text && text !== name) names[name] = text;
}
const heroes: Record<string, string> = {};
for (const [id, name] of Object.entries(HEROES)) {
  const text = zh(name);
  if (!text) throw new Error(`no zh-CN name for hero ${name}`);
  heroes[id] = text;
}

fs.writeFileSync(
  target,
  `${JSON.stringify({ heroes, names, cards }, null, 2)}\n`
);
console.log(
  `wrote ${Object.keys(heroes).length} heroes and ${Object.keys(names).length} of ${english.size} names`
);
