import {
  CardDetail,
  enchantments,
  type CardDefinition,
  type Enchantment
} from '@bpp/ui';
import { card, pinned, tiers, enchants } from '../cards';
import data from '../demo-data/definition.json';
import { type DemoProps, translate } from '../copy';
export default function Detail({ lang }: DemoProps) {
  const t = (zh: string, en: string) => translate(lang, zh, en);
  return (
    <CardDetail
      card={card(lang, pinned[0])}
      definition={data[lang] as CardDefinition}
      labels={{
        sizes: {
          Small: t('小型', 'Small'),
          Medium: t('中型', 'Medium'),
          Large: t('大型', 'Large')
        },
        enchantments: t('附魔', 'Enchantments'),
        ammo: t('弹药', 'Ammo'),
        cooldown: t('冷却', 'Cooldown'),
        seconds: t('秒', 's'),
        unavailable: t('不可用', 'Unavailable')
      }}
      tagLabels={data.tags[lang]}
      tierLabels={lang === 'zh' ? tiers : {}}
      enchantLabels={
        lang === 'zh'
          ? enchants
          : (Object.fromEntries(enchantments.map((e) => [e, e])) as Record<
              Enchantment,
              string
            >)
      }
    />
  );
}
