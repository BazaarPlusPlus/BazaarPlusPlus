import { CardTile, enchantments } from '@bpp/ui';
import { card, pinned } from '../cards';
import type { DemoProps } from '../copy';
export default function Gallery({ lang }: DemoProps) {
  return (
    <div className="gallery">
      {enchantments.map((enchant, index) => (
        <CardTile key={enchant} card={card(lang, pinned[index], enchant)} />
      ))}
    </div>
  );
}
