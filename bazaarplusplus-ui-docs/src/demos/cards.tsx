import {
  CardChip,
  CardTile,
  cardTiers,
  enchantments
} from '@bazaarplusplus/ui/game';
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
        {enchantments.flatMap((enchant, index) => {
          const items = [
            <CardTile
              key={enchant}
              card={{ ...card(lang, pinned[index], enchant), onOpen: open }}
            />
          ];
          if (enchant === 'Tiny' && pinned[16]) {
            items.push(
              <CardTile
                key="Tiny-large"
                card={{ ...card(lang, pinned[16], enchant), onOpen: open }}
              />
            );
          }
          return items;
        })}
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
