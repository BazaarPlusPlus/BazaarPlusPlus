// Renders the stats and hero images from real Bundles; never sends a QQ message.
//
//   bun test/preview.ts <bundle dir> [--name <shown name>] [--art <Card Art dir>]
//
// <bundle dir> holds one player's *.bundle files (for example fetched with
// `wrangler r2 object get`). Hero metrics, builds and names come from the CDN.
// --art serves a local `just bot::card-art` output in place of the published
// card-art/ set. Images go to artifacts/qqbot/.
import fs from 'node:fs/promises';
import path from 'node:path';
import { parseArgs } from 'node:util';
import { chromium } from 'playwright';
import { decodeBundle, digest } from '../src/bundle';
import type { Names } from '../src/commands';
import { CARD_ART, builds, cardArt, heroes, names } from '../src/data';
import type { Run } from '../src/model';
import { heroHtml, statsHtml, type ReportInput } from '../src/report';

const { values, positionals } = parseArgs({
  allowPositionals: true,
  options: { name: { type: 'string' }, art: { type: 'string' } }
});
const source = positionals[0];
if (!source)
  throw new Error(
    'usage: bun test/preview.ts <bundle dir> [--name <name>] [--art <dir>]'
  );
const signal = AbortSignal.timeout(60_000);
const loaded: Run[] = [];
for (const file of (await fs.readdir(source)).filter((f) =>
  f.endsWith('.bundle')
)) {
  const bytes = new Uint8Array(await fs.readFile(path.join(source, file)));
  const length = new DataView(bytes.buffer).getUint32(12);
  const manifest = JSON.parse(
    new TextDecoder().decode(bytes.subarray(16, 16 + length))
  );
  const ref = {
    bundle_id: manifest.bundle_id,
    run_id: manifest.run.run_id,
    sha256: await digest(bytes),
    download_url: '',
    stored_at_ms: 0
  };
  loaded.push(await decodeBundle(bytes, ref, manifest.run.player_account_id));
}
const runs = loaded
  .filter((run) => run.status === 'completed' && run.ended !== null)
  .sort((a, b) => a.ended! - b.ended!);
if (!runs.length) throw new Error(`no completed Run in ${source}`);
const accounts = new Set(runs.map((run) => run.account));
if (accounts.size !== 1)
  throw new Error(`${source} holds ${accounts.size} players; use one`);

const [translations, snapshot, corpus, published] = await Promise.all([
  names(signal).catch((): Names => ({ heroes: {}, names: {} })),
  heroes(signal).catch(() => null),
  builds(signal).catch(() => null),
  values.art ? Promise.resolve({}) : cardArt(signal).catch(() => ({}))
]);
const art = values.art
  ? (JSON.parse(await fs.readFile(path.join(values.art, 'index.json'), 'utf8'))
      .cards as Record<string, string>)
  : published;
const day = 86_400_000;
const before = runs.at(-1)!.ended! + 1;
const input: ReportInput = {
  name: values.name ?? `玩家 ${runs[0].account.slice(0, 8)}`,
  runs,
  names: translations,
  // Beijing midnight at or before the first Run.
  from:
    Math.floor((runs[0].ended! + 8 * 3_600_000) / day) * day - 8 * 3_600_000,
  before,
  art,
  skipped: 0
};
const counts = new Map<string, number>();
for (const run of runs) counts.set(run.hero, (counts.get(run.hero) ?? 0) + 1);
const hero = [...counts].sort((a, b) => b[1] - a[1])[0][0];

const directory = path.resolve(import.meta.dirname, '../../artifacts/qqbot');
await fs.mkdir(directory, { recursive: true });
const browser = await chromium.launch({ headless: true });
try {
  const page = await browser.newPage({
    viewport: { width: 720, height: 100 },
    deviceScaleFactor: 2,
    javaScriptEnabled: false
  });
  if (values.art) {
    const local = values.art;
    await page.route(`${CARD_ART}/**`, (route) =>
      route.fulfill({
        path: path.join(
          local,
          path.basename(new URL(route.request().url()).pathname)
        )
      })
    );
  }
  for (const [name, html] of [
    ['stats', statsHtml(input)],
    ...(snapshot
      ? [['hero', heroHtml(hero, snapshot, corpus, translations, art)]]
      : [])
  ]) {
    await page.setContent(html, { waitUntil: 'load' });
    const file = path.join(directory, `${name}.png`);
    await page.locator('main').screenshot({ path: file });
    const jpeg = await page
      .locator('main')
      .screenshot({ type: 'jpeg', quality: 85 });
    const overflow = await page.evaluate(
      () => document.documentElement.scrollWidth > 720
    );
    if (overflow) throw new Error(`${name} overflows`);
    const bytes = (await fs.stat(file)).size;
    const box = await page.locator('main').boundingBox();
    const size = `${box?.width}×${box?.height}`;
    console.log(
      `${name}: ${file} (${(bytes / 1048576).toFixed(2)} MB PNG, ${(jpeg.length / 1048576).toFixed(2)} MB JPEG, ${size} px)`
    );
  }
} finally {
  await browser.close();
}
