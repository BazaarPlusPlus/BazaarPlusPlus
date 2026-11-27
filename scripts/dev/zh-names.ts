// Extract Simplified Chinese hero, card, skill, and enchantment names from the
// local game cache (GameData.db + translations/zh-CN.bytes). The translation
// table indexes Chinese strings by the MD5 of their English text.
import crypto from 'node:crypto';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
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

export interface ZhNamesDocument {
  heroes: Record<string, string>;
  names: Record<string, string>;
  cards: Record<string, string>;
}

export function defaultCacheDir(
  platform: NodeJS.Platform = process.platform,
  home = os.homedir(),
  env: NodeJS.ProcessEnv = process.env
): string {
  if (platform === 'win32') {
    const base = env.USERPROFILE || home;
    return path.win32.join(
      base,
      'AppData',
      'LocalLow',
      'Tempo Storm',
      'The Bazaar',
      'prod',
      'cache'
    );
  }
  if (platform === 'linux') {
    return path.posix.join(
      home,
      '.config/unity3d/Tempo Storm/The Bazaar/prod/cache'
    );
  }
  return path.posix.join(
    home,
    'Library/Application Support/com.TempoStorm.TheBazaar/prod/cache'
  );
}

export function extractZhNames(cacheDir: string): {
  document: ZhNamesDocument;
  englishCount: number;
} {
  const translations = new DatabaseSync(
    path.join(cacheDir, 'translations', 'zh-CN.bytes'),
    { readOnly: true }
  );
  const game = new DatabaseSync(path.join(cacheDir, 'GameData.db'), {
    readOnly: true
  });
  try {
    const lookup = translations.prepare(
      'SELECT text FROM translation WHERE hash = ?'
    );
    const zh = (english: string): string | undefined => {
      const hash = crypto.createHash('md5').update(english).digest('hex');
      return (lookup.get(hash) as { text: string } | undefined)?.text;
    };

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
      .all() as { name: string | null }[]) {
      if (name) english.add(name);
    }

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
    return {
      document: { heroes, names, cards },
      englishCount: english.size
    };
  } finally {
    translations.close();
    game.close();
  }
}

export function cliMain(argv: readonly string[]): number {
  const [target, cacheArg] = argv;
  if (!target || target === '--help') {
    throw new Error(
      'Usage: bun scripts/dev/zh-names.ts <out.json> [cache dir]'
    );
  }
  const { document, englishCount } = extractZhNames(
    cacheArg ?? defaultCacheDir()
  );
  fs.writeFileSync(target, `${JSON.stringify(document, null, 2)}\n`);
  console.log(
    `wrote ${Object.keys(document.heroes).length} heroes and ${Object.keys(document.names).length} of ${englishCount} names`
  );
  return 0;
}

if (import.meta.main) {
  try {
    process.exitCode = cliMain(process.argv.slice(2));
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    process.exitCode = 1;
  }
}
