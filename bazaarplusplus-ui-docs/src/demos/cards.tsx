import { CardChip, CardTile, cardTiers, enchantments } from '@bpp/ui';
import { card, pinned } from '../cards';
import { type DemoProps, translate } from '../copy';
const open = () => {};
export default function Cards({ lang }: DemoProps) {
  const first = card(lang, pinned[0]);
  return (
    <div className="grid gap-4">
      <div className="gallery">
        {cardTiers.map((tier) => (
          <CardTile
            key={tier}
            card={{
              ...card(
                lang,
                pinned.find((p) => p.tier === tier)!
              ),
              onOpen: open
            }}
          />
        ))}
      </div>
      <div className="gallery">
        {enchantments.map((enchant, index) => (
          <CardTile
            key={enchant}
            card={{ ...card(lang, pinned[index], enchant), onOpen: open }}
          />
        ))}
      </div>
      <div className="flex flex-wrap gap-3">
        <CardChip card={first} title={first.name} />
        <CardChip
          placeholder
          card={{
            ...first,
            art: null,
            name: translate(lang, '缺少插画', 'Missing illustration')
          }}
          title=""
        />
      </div>
    </div>
  );
}
