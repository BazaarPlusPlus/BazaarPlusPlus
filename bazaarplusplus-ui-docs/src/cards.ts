import { createElement } from 'react';
import {
  CardDetail,
  gameTerms,
  type CardDefinition,
  type CardFx,
  type CardSize,
  type CardTier,
  type Enchantment,
  type FxMaterial,
  type GameCard
} from '@bazaarplusplus/ui/game';
import manifest from '../public/card-art/manifest.json';
import definitions from './demo-data/definition.json';
import type { Locale } from './copy';
const sizes: Record<string, CardSize> = { Small: 1, Medium: 2, Large: 3 };
export const pinned = manifest.cards;
/** CardDetail for a pinned card or skill: the detail demo and every hover preview. */
export function detail(
  lang: Locale,
  shown: GameCard,
  id: string,
  preview = true
) {
  const entry = (definitions.cards as Record<string, Record<Locale, unknown>>)[
    id
  ];
  return createElement(CardDetail, {
    card: shown,
    definition: entry?.[lang] as CardDefinition | undefined,
    labels: gameTerms[lang].card,
    tagLabels: definitions.tags[lang],
    tierLabels: gameTerms[lang].tiers,
    preview,
    enchantLabels: gameTerms[lang].enchantments
  });
}
export function skill(
  lang: Locale,
  s: (typeof manifest.skills)[number]
): GameCard & { id: string } {
  const tier = s.tier as CardTier;
  const result = {
    id: s.id,
    name: s[lang],
    art: '/card-art/' + s.file,
    tier,
    tierLabel: gameTerms[lang].tiers[tier],
    enchant: null,
    enchantLabel: ''
  };
  return { ...result, preview: detail(lang, result, s.id) };
}
export const pinnedSkills = manifest.skills;
async function asset(file: string) {
  const response = await fetch('/card-art/' + file);
  if (!response.ok) throw new Error('Unavailable Card Art asset: ' + file);
  return response;
}
function effectiveSize(base: CardSize, enchant: Enchantment | null): CardSize {
  if (enchant !== 'Tiny') return base;
  if (base === 1)
    throw new Error('Tiny enchantment applies only to Medium and Large cards');
  return (base - 1) as CardSize;
}
const sources = pinned.map((p): CardFx => ({
  slots: sizes[p.size],
  tiny: false,
  material: async () => (await (await asset(p.fx)).json()) as FxMaterial,
  texture: async (file) => (await asset(file)).blob()
}));
const tinySources = pinned.map((p, index): CardFx => ({
  ...sources[index],
  slots: sizes[p.size] > 1 ? ((sizes[p.size] - 1) as CardSize) : 1,
  tiny: sizes[p.size] > 1
}));
export function card(
  lang: Locale,
  p: (typeof pinned)[number],
  enchant: Enchantment | null = null
): GameCard & { id: string; size: CardSize } {
  const tier = p.tier as CardTier;
  const size = effectiveSize(sizes[p.size], enchant);
  const result = {
    id: p.id,
    name: p[lang],
    art: '/card-art/' + p.file,
    tier,
    tierLabel: gameTerms[lang].tiers[tier],
    enchant,
    enchantLabel: enchant ? gameTerms[lang].enchantments[enchant] : '',
    size,
    fx: enchant
      ? (enchant === 'Tiny' ? tinySources : sources)[pinned.indexOf(p)]
      : undefined
  };
  return { ...result, preview: detail(lang, result, p.id) };
}
