import { useState } from 'react';
import type { CardSize, CardTier, Enchantment } from './types';

import type { CardFx } from './enchantment-fx/program';
import { FxFace } from './enchantment-fx/Face';

export type GameCard = {
  fx?: CardFx;
  name: string;
  art: string | null;
  tier: CardTier | null;
  tierLabel: string;
  enchant: Enchantment | null;
  enchantLabel: string;
  /** Board width in slots; the square art stretches to 1:2, 2:2 or 3:2 as in the game. */
  size?: CardSize | null;
  onOpen?: () => void;
  change?: 'new' | 'tier-up' | null;
};

export function CardArt({
  src,
  alt,
  fallback
}: {
  src: string | null;
  alt: string;
  fallback: string;
}) {
  const [failed, setFailed] = useState<string | null>(null);
  if (!src || src === failed)
    return fallback && <span className="bpp-card-fallback">{fallback}</span>;
  return (
    <img
      className="bpp-card-art"
      src={src}
      alt={alt}
      decoding="async"
      loading="lazy"
      onError={() => setFailed(src)}
    />
  );
}

export function EnchantLabel({
  enchant,
  label
}: {
  enchant: Enchantment | null;
  label: string;
}) {
  return (
    label && (
      <span className={`bpp-enchant ${enchantClass(enchant)}`}>{label}</span>
    )
  );
}

export function TierLabel({ label }: { label: string }) {
  return label && <span className="bpp-tier-label">{label}</span>;
}

function enchantClass(enchant: Enchantment | null) {
  return enchant ? `bpp-enchant-${enchant.toLowerCase()}` : '';
}

function tierClass(tier: CardTier | null) {
  return tier ? `bpp-tier-${tier.toLowerCase()}` : '';
}

export function CardTile({ card }: { card: GameCard }) {
  const Element = card.onOpen ? 'button' : 'span';
  return (
    <Element
      type={card.onOpen ? 'button' : undefined}
      onClick={card.onOpen}
      data-size={card.size ?? undefined}
      className={`bpp-card ${tierClass(card.tier)}`}
      title={[card.name, card.tierLabel, card.enchantLabel]
        .filter(Boolean)
        .join(' · ')}
    >
      <span className="bpp-card-face">
        <CardArt src={card.art} alt={card.name} fallback={card.name} />
        {card.fx && card.enchant && (
          <FxFace source={card.fx} enchant={card.enchant} />
        )}
      </span>
      <TierLabel label={card.tierLabel} />
      <EnchantLabel enchant={card.enchant} label={card.enchantLabel} />
      {card.change && (
        <span
          aria-hidden="true"
          className="bpp-card-change"
          data-change={card.change}
        >
          {card.change === 'new' ? '+' : '↑'}
        </span>
      )}
    </Element>
  );
}

/** A skill: round, ringed in its tier, named only in its title and alt text. */
export function SkillTile({ card }: { card: GameCard }) {
  const Element = card.onOpen ? 'button' : 'span';
  return (
    <Element
      type={card.onOpen ? 'button' : undefined}
      onClick={card.onOpen}
      className={`bpp-skill-tile ${tierClass(card.tier)}`}
      title={[card.name, card.tierLabel].filter(Boolean).join(' · ')}
    >
      <CardArt src={card.art} alt={card.name} fallback={card.name} />
      <TierLabel label={card.tierLabel} />
    </Element>
  );
}

export function CardChip({
  card,
  title,
  placeholder = false
}: {
  card: GameCard;
  title: string;
  placeholder?: boolean;
}) {
  const Element = card.onOpen ? 'button' : 'span';
  return (
    <Element
      type={card.onOpen ? 'button' : undefined}
      onClick={card.onOpen}
      className={`bpp-card-chip ${tierClass(card.tier)}`}
      title={title}
    >
      {(card.art || placeholder) && (
        <span className="bpp-card-chip-art" data-size={card.size ?? undefined}>
          <CardArt src={card.art} alt="" fallback="" />
          {card.fx && card.enchant && (
            <FxFace source={card.fx} enchant={card.enchant} still />
          )}
        </span>
      )}
      <span>{card.name}</span>
      <TierLabel label={card.tierLabel} />
      <EnchantLabel enchant={card.enchant} label={card.enchantLabel} />
    </Element>
  );
}
