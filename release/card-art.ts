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

// Card Art (root ADR 0020): the game's item and skill art, extracted by
// bazaarplusplus-bot/tools/card-art/extract.py and served at
// https://cdn.bazaarplusplus.com/card-art/. .github/workflows/card-art.yml runs
// stale, download, the extractor and publish in that order.
export const CARD_ART_PREFIX = 'card-art';
export const CARD_ART_INDEX = 'index.json';
// The GameData the game itself downloads; the copy in the depot is months older.
export const GAME_DATA_URL =
  'https://data.playthebazaar.com/static/GameData.db.zip';
// Every desktop depot ships the same art; Windows is the one most players run.
const CARD_ART_DEPOT = STEAM_DEPOTS.windows;

/** The two inputs a Card Art set is built from. */
export interface CardArtSource {
  buildid: string;
  gameData: string;
}

/** The part of a published index.json this module reads. */
interface CardArtIndex {
  game?: Partial<CardArtSource>;
}

const defaultRunner = () =>
  steamToolRunner(path.join(WORKSPACE_ROOT, 'bazaarplusplus-mod'));

async function gameDataEtag(): Promise<string> {
  const response = await fetch(GAME_DATA_URL, { method: 'HEAD' });
  const etag = response.headers.get('etag');
  if (!response.ok || !etag)
    throw new Error(`${GAME_DATA_URL} gave ${response.status} and no ETag`);
  return etag.replaceAll('"', '');
}

/**
 * Whether the online branch or GameData has moved past the published index.
 * A missing previous index is stale.
 */
export async function cardArtStale(
  previousIndex: string,
  runTool: SteamToolRunner = defaultRunner(),
  etag: () => Promise<string> = gameDataEtag
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
  const gameData = await etag();
  const published = fs.existsSync(previousIndex)
    ? (JSON.parse(fs.readFileSync(previousIndex, 'utf8')) as CardArtIndex)
    : {};
  return {
    stale:
      published.game?.buildid !== String(buildid) ||
      published.game?.gameData !== gameData,
    buildid: String(buildid),
    gameData
  };
}

/**
 * Downloads what the extractor reads: the Steam files into <out>/Data and the
 * live GameData to <out>/GameData.db.zip.
 */
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
  const response = await fetch(GAME_DATA_URL);
  if (!response.ok) throw new Error(`${GAME_DATA_URL} gave ${response.status}`);
  fs.writeFileSync(
    path.join(out, 'GameData.db.zip'),
    Buffer.from(await response.arrayBuffer())
  );
  const etag = response.headers.get('etag')?.replaceAll('"', '');
  if (!etag) throw new Error(`${GAME_DATA_URL} gave no ETag`);
  return { buildid: String(buildid), gameData: etag };
}

/**
 * Uploads every art file in the directory, then index.json, so a reader never
 * sees an index that points at a missing file. File names are content digests,
 * so unchanged art is a no-op and nothing is ever deleted; entries the
 * extractor kept from the previous index point at files already uploaded.
 */
export async function publishCardArt(
  directory: string,
  store: Pick<ObjectStore, 'get' | 'put'> = r2StoreFromEnvironment(
    process.env,
    { bucket: RELEASE_BUCKET, prefix: CARD_ART_PREFIX }
  )
): Promise<number> {
  const indexBytes = fs.readFileSync(path.join(directory, CARD_ART_INDEX));
  JSON.parse(indexBytes.toString('utf8'));
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
  await putReplaceable(store, CARD_ART_INDEX, indexBytes, 'application/json');
  return files.length;
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
