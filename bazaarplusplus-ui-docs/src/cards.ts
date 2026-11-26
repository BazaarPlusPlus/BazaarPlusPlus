import { createElement } from 'react';
import {
  CardDetail,
  enchantments,
  type CardDefinition,
  type CardFx,
  type CardSize,
  type CardTier,
  type Enchantment,
  type FxMaterial,
  type GameCard
} from '@bpp/ui';
import manifest from '../public/card-art/manifest.json';
import definitions from './demo-data/definition.json';
import { translate, type Locale } from './copy';
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
/** CardDetail for a pinned card or skill: the detail demo and every hover preview. */
export function detail(
  lang: Locale,
  shown: GameCard,
  id: string,
  preview = true
) {
  const t = (zh: string, en: string) => translate(lang, zh, en);
  const entry = (definitions.cards as Record<string, Record<Locale, unknown>>)[
    id
  ];
  return createElement(CardDetail, {
    card: shown,
    definition: entry?.[lang] as CardDefinition | undefined,
    labels: {
      sizes: {
        Small: t('小型', 'Small'),
        Medium: t('中型', 'Medium'),
        Large: t('大型', 'Large')
      },
      enchantments: t('附魔', 'Enchantments'),
      allEnchantments: t('显示全部附魔', 'Show all enchantments'),
      ammo: t('弹药', 'Ammo'),
      cooldown: t('冷却', 'Cooldown'),
      seconds: t('秒', 's'),
      unavailable: t('不可用', 'Unavailable')
    },
    tagLabels: definitions.tags[lang],
    tierLabels: lang === 'zh' ? tiers : {},
    preview,
    enchantLabels:
      lang === 'zh'
        ? enchants
        : (Object.fromEntries(enchantments.map((e) => [e, e])) as Record<
            Enchantment,
            string
          >)
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
    tierLabel: lang === 'zh' ? tiers[tier] : tier,
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
  const result = {
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
  return { ...result, preview: detail(lang, result, p.id) };
}
