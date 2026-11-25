import { useState } from 'react';
import type { CardTier, Enchantment } from './types';

export type GameCard = {
  name: string;
  art: string | null;
  tier: CardTier | null;
  tierLabel: string;
  enchant: Enchantment | null;
  enchantLabel: string;
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

export function CardTile({
  card,
  caption = false
}: {
  card: GameCard;
  caption?: boolean;
}) {
  const Element = card.onOpen ? 'button' : 'span';
  return (
    <Element
      type={card.onOpen ? 'button' : undefined}
      onClick={card.onOpen}
      className={`bpp-card ${tierClass(card.tier)} ${enchantClass(card.enchant)} ${card.enchant ? 'bpp-card-enchant' : ''} ${caption ? 'bpp-card-caption' : ''}`}
      title={[card.name, card.tierLabel, card.enchantLabel]
        .filter(Boolean)
        .join(' · ')}
    >
      <span className="bpp-card-face">
        <CardArt
          src={card.art}
          alt={caption ? '' : card.name}
          fallback={caption ? '' : card.name}
        />
      </span>
      {caption && <span className="bpp-card-name">{card.name}</span>}
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
        <span className="bpp-card-chip-art">
          <CardArt src={card.art} alt="" fallback="" />
        </span>
      )}
      <span>{card.name}</span>
      <TierLabel label={card.tierLabel} />
      <EnchantLabel enchant={card.enchant} label={card.enchantLabel} />
    </Element>
  );
}
