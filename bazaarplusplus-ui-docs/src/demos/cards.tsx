import { CardChip, CardTile, cardTiers } from '@bpp/ui';
import { card, pinned } from '../cards';
import { type DemoProps, translate } from '../copy';
export default function Cards({ lang }: DemoProps) {
  const first = card(lang, pinned[0]);
  return (
    <div className="grid gap-4">
      <div className="gallery">
        {cardTiers.map((tier) => (
          <CardTile
            key={tier}
            caption
            card={card(
              lang,
              pinned.find((p) => p.tier === tier)!
            )}
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
