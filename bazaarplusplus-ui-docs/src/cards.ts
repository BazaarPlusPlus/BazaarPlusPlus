import {
  type CardFx,
  type CardSize,
  type CardTier,
  type Enchantment,
  type FxMaterial,
  type GameCard
} from '@bpp/ui';
import manifest from '../public/card-art/manifest.json';
import type { Locale } from './copy';
export const tiers = {
  Bronze: '青铜',
  Silver: '白银',
  Gold: '黄金',
  Diamond: '钻石',
  Legendary: '传说'
};
export const enchants = {
  Deadly: '致命',
  Ethereal: '空灵',
  Fiery: '炽焰',
  Golden: '黄金',
  Heavy: '沉重',
  Icy: '寒冰',
  Mystical: '神秘',
  Obsidian: '黑曜石',
  Radiant: '辉耀',
  Restorative: '回复',
  Shielded: '护盾',
  Shiny: '闪亮',
  Tiny: '迷你',
  Toxic: '毒素',
  Turbo: '疾速',
  Mossy: '长青'
};
const sizes: Record<string, CardSize> = { Small: 1, Medium: 2, Large: 3 };
export const pinned = manifest.cards;
async function asset(file: string) {
  const response = await fetch('/card-art/' + file);
  if (!response.ok) throw new Error('Unavailable Card Art asset: ' + file);
  return response;
}
const sources = pinned.map((p): CardFx => ({
  slots: sizes[p.size],
  tiny: false,
  material: async () => (await (await asset(p.fx)).json()) as FxMaterial,
  texture: async (file) => (await asset(file)).blob()
}));
const tinySources = sources.map((source) => ({ ...source, tiny: true }));
export function card(
  lang: Locale,
  p: (typeof pinned)[number],
  enchant: Enchantment | null = null
): GameCard & { id: string; size: CardSize } {
  const tier = p.tier as CardTier;
  return {
    id: p.id,
    name: p[lang],
    art: '/card-art/' + p.file,
    tier,
    tierLabel: lang === 'zh' ? tiers[tier] : tier,
    enchant,
    enchantLabel: enchant ? (lang === 'zh' ? enchants[enchant] : enchant) : '',
    size: sizes[p.size],
    fx: enchant
      ? (enchant === 'Tiny' ? tinySources : sources)[pinned.indexOf(p)]
      : undefined
  };
}
