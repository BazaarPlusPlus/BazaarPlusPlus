import { mkdirSync, writeFileSync } from 'node:fs';
import { createHash } from 'node:crypto';
import type { FxMaterial } from '../../design/ui/game/enchantment-fx/program';
import type { CardDefinition } from '../../design/ui/game/CardDetail';
import run from '../src/demo-data/run.json';
import { cardTiers } from '../../design/ui/game/types';

const origin = 'https://cdn.bazaarplusplus.com/card-art/';
const dir = 'public/card-art/';
async function fetchFile(name: string) {
  const response = await fetch(origin + name);
  if (!response.ok) throw new Error(name + ' ' + response.status);
  return Buffer.from(await response.arrayBuffer());
}
const raw = await fetchFile('index.json');
type Files = Record<string, string>;
type Text = {
  cards: Record<
    string,
    Omit<CardDefinition, 'enchantments'> & {
      type: string;
      enchantments: Record<string, number>;
    }
  >;
  enchantments: CardDefinition['enchantments'][string][];
  tagLabels: Files;
};
// Card Text stores each distinct enchantment once; a card names it by position.
function definition(text: Text, id: string): CardDefinition {
  const card = text.cards[id];
  return {
    ...card,
    enchantments: Object.fromEntries(
      Object.entries(card.enchantments).map(([name, at]) => [
        name,
        text.enchantments[at]
      ])
    )
  };
}
const index = JSON.parse(raw.toString()) as {
  cards: Files;
  fx?: Files;
  game: unknown;
  text: Files;
};
if (!index.fx || !Object.keys(index.fx).length)
  throw new Error('Published Card Art index has no fx map');
const zh = JSON.parse((await fetchFile(index.text.zh)).toString()) as Text;
const en = JSON.parse((await fetchFile(index.text.en)).toString()) as Text;
const items = Object.keys(index.fx)
  .sort()
  .filter(
    (id) =>
      zh.cards[id]?.type === 'Item' &&
      zh.cards[id].title &&
      en.cards[id]?.title &&
      index.cards[id]
  );
const ids = new Set(run.moments.map((m) => m.card));
for (const tier of cardTiers)
  for (const size of ['Small', 'Medium', 'Large']) {
    const match = items.find(
      (id) => zh.cards[id].startingTier === tier && zh.cards[id].size === size
    );
    if (match) ids.add(match);
  }
for (const id of items) {
  if (ids.size >= 16) break;
  ids.add(id);
}
mkdirSync(dir, { recursive: true });
const files = new Set<string>();
const cards = [];
const detailId = [...ids][0];
for (const id of ids) {
  const file = index.cards[id],
    fx = index.fx[id];
  // Download one descriptor at a time to bound CDN traffic.
  // oxlint-disable-next-line no-await-in-loop
  const material = await fetchFile(fx);
  writeFileSync(dir + fx, material);
  files.add(file);
  const descriptor = JSON.parse(material.toString()) as FxMaterial;
  for (const texture of Object.values(descriptor.textures)) {
    if (texture.file) files.add(texture.file);
  }
  cards.push({
    id,
    file,
    fx,
    zh: zh.cards[id].title,
    en: en.cards[id].title,
    size: zh.cards[id].size,
    tier: zh.cards[id].startingTier
  });
}
const names = [...files].sort();
for (let i = 0; i < names.length; i += 6)
  // Keep at most six asset downloads in flight.
  // oxlint-disable-next-line no-await-in-loop
  await Promise.all(
    names
      .slice(i, i + 6)
      .map(async (name) => writeFileSync(dir + name, await fetchFile(name)))
  );
writeFileSync(
  dir + 'manifest.json',
  JSON.stringify(
    {
      origin,
      indexSha256: createHash('sha256').update(raw).digest('hex'),
      game: index.game,
      text: index.text,
      cards,
      textures: names
    },
    null,
    2
  ) + '\n'
);
writeFileSync(
  'src/demo-data/definition.json',
  JSON.stringify(
    {
      id: detailId,
      zh: definition(zh, detailId),
      en: definition(en, detailId),
      tags: { zh: zh.tagLabels, en: en.tagLabels }
    },
    null,
    2
  ) + '\n'
);
