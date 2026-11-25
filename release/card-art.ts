import fs from 'node:fs';
import path from 'node:path';
import {
  STEAM_APP_ID,
  STEAM_DEPOTS,
  steamToolRunner
} from './game-assembly-snapshot.ts';
import type { SteamToolRunner } from './game-assembly-snapshot.ts';
import { WORKSPACE_ROOT } from './product.ts';
import {
  RELEASE_BUCKET,
  putImmutable,
  putReplaceable,
  r2StoreFromEnvironment
} from './r2-store.ts';
import type { ObjectStore } from './r2-store.ts';
import { STEAM_BRANCHES } from './snapshot-lock.ts';

export const CARD_ART_PREFIX = 'card-art';
export const CARD_ART_INDEX = 'index.json';
// The GameData the game itself downloads; the copy in the depot is months older.
export const GAME_DATA_URL =
  'https://data.playthebazaar.com/static/GameData.db.zip';
const ZH_URL = 'https://data.playthebazaar.com/static/translations/zh-CN.bytes';
const locales = ['en', 'zh'] as const;

function isTextFile(locale: string, name: unknown): name is string {
  return (
    typeof name === 'string' &&
    new RegExp(`^text-${locale}-[0-9a-f]{20}\\.json$`).test(name)
  );
}

// Every desktop depot ships the same art.
const CARD_ART_DEPOT = STEAM_DEPOTS.windows;

export interface CardArtSource {
  buildid: string;
  gameData: string;
  zh: string;
}

interface CardArtIndex {
  version?: number;
  text?: Partial<Record<(typeof locales)[number], string>>;
  game?: Partial<CardArtSource>;
  /** TemplateId → English title; an index from before it existed is republished. */
  names?: Record<string, string>;
}

const defaultRunner = () =>
  steamToolRunner(path.join(WORKSPACE_ROOT, 'bazaarplusplus-mod'));

async function sourceEtag(url: string): Promise<string> {
  const response = await fetch(url, { method: 'HEAD' });
  const etag = response.headers.get('etag');
  if (!response.ok || !etag)
    throw new Error(`${url} gave ${response.status} and no ETag`);
  return etag.replaceAll('"', '');
}

export async function cardArtStale(
  previousIndex: string,
  runTool: SteamToolRunner = defaultRunner(),
  etag: (url: string) => Promise<string> = sourceEtag
): Promise<CardArtSource & { stale: boolean }> {
  const { buildid } = runTool([
    'state',
    '--app',
    String(STEAM_APP_ID),
    '--branch',
    STEAM_BRANCHES.online,
    '--depots',
    String(CARD_ART_DEPOT)
  ]);
  const [gameData, zh] = await Promise.all([etag(GAME_DATA_URL), etag(ZH_URL)]);
  const published = fs.existsSync(previousIndex)
    ? (JSON.parse(fs.readFileSync(previousIndex, 'utf8')) as CardArtIndex)
    : {};
  return {
    stale:
      published.game?.buildid !== String(buildid) ||
      published.game?.gameData !== gameData ||
      published.game?.zh !== zh ||
      published.version !== 2 ||
      locales.some((locale) => !isTextFile(locale, published.text?.[locale])) ||
      published.names === undefined,
    buildid: String(buildid),
    gameData,
    zh
  };
}

export async function downloadCardArt(
  out: string,
  runTool: SteamToolRunner = defaultRunner()
): Promise<CardArtSource> {
  const { buildid } = runTool([
    'card-art',
    '--app',
    String(STEAM_APP_ID),
    '--branch',
    STEAM_BRANCHES.online,
    '--depot',
    String(CARD_ART_DEPOT),
    '--out',
    path.resolve(out)
  ]);
  async function download(url: string, filename: string) {
    const response = await fetch(url);
    if (!response.ok) throw new Error(`${url} gave ${response.status}`);
    const etag = response.headers.get('etag')?.replaceAll('"', '');
    if (!etag) throw new Error(`${url} gave no ETag`);
    fs.writeFileSync(
      path.join(out, filename),
      Buffer.from(await response.arrayBuffer())
    );
    return etag;
  }
  const gameData = await download(GAME_DATA_URL, 'GameData.db.zip');
  const zh = await download(ZH_URL, 'zh-CN.bytes');
  return { buildid: String(buildid), gameData, zh };
}

/** Publish the index last so readers can resolve every referenced image and locale file. */
export async function publishCardArt(
  directory: string,
  store: Pick<ObjectStore, 'get' | 'put'> = r2StoreFromEnvironment(
    process.env,
    { bucket: RELEASE_BUCKET, prefix: CARD_ART_PREFIX }
  )
): Promise<number> {
  const indexBytes = fs.readFileSync(path.join(directory, CARD_ART_INDEX));
  const index = JSON.parse(indexBytes.toString('utf8')) as CardArtIndex;
  const files = fs
    .readdirSync(directory)
    .filter((name) => name.endsWith('.webp'))
    .sort();
  if (!files.length) throw new Error(`${directory} holds no Card Art`);
  for (const file of files) {
    if (!/^[0-9a-f]{20}\.webp$/.test(file))
      throw new Error(`unexpected Card Art file: ${file}`);
    await putImmutable(
      store,
      file,
      fs.readFileSync(path.join(directory, file)),
      'image/webp'
    );
  }
  for (const locale of locales) {
    const name = index.text?.[locale];
    if (!isTextFile(locale, name))
      throw new Error(`missing Card Text file for ${locale}`);
    await putImmutable(
      store,
      name,
      fs.readFileSync(path.join(directory, name)),
      'application/json'
    );
  }
  await putReplaceable(store, CARD_ART_INDEX, indexBytes, 'application/json');
  return files.length + locales.length;
}

if (import.meta.main) {
  const [command, argument] = process.argv.slice(2);
  try {
    if (!argument)
      throw new Error(
        'usage: bun release/card-art.ts <stale <previous index.json> | download <dir> | publish <dir>>'
      );
    if (command === 'stale')
      process.stdout.write(`${JSON.stringify(await cardArtStale(argument))}\n`);
    else if (command === 'download')
      process.stdout.write(
        `${JSON.stringify(await downloadCardArt(argument))}\n`
      );
    else if (command === 'publish')
      process.stdout.write(
        `Published ${await publishCardArt(argument)} Card Art files\n`
      );
    else throw new Error(`unknown command ${command}`);
  } catch (error) {
    process.stderr.write(`${error instanceof Error ? error.message : error}\n`);
    process.exitCode = 2;
  }
}
