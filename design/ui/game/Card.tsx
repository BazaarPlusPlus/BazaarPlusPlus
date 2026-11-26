import {
  useLayoutEffect,
  useRef,
  useState,
  type FocusEvent,
  type PointerEvent,
  type ReactNode
} from 'react';
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
  /** Shown beside the tile while a mouse rests on it or keyboard focus reaches it. */
  preview?: ReactNode;
  change?: 'new' | 'tier-up' | null;
};

/** Places the preview beside its anchor, flipping left and clamping to the viewport. */
function Preview({
  anchor,
  children
}: {
  anchor: HTMLElement;
  children: ReactNode;
}) {
  const ref = useRef<HTMLDivElement>(null);
  useLayoutEffect(() => {
    const panel = ref.current;
    if (!panel) return;
    panel.showPopover();
    const place = () => {
      const gap = 8;
      const box = anchor.getBoundingClientRect();
      const { offsetWidth: width, offsetHeight: height } = panel;
      const left =
        box.right + gap + width <= innerWidth
          ? box.right + gap
          : Math.max(gap, box.left - gap - width);
      const top = Math.max(gap, Math.min(box.top, innerHeight - height - gap));
      panel.style.left = `${left}px`;
      panel.style.top = `${top}px`;
    };
    place();
    // The definition may arrive after the panel opens.
    const sizes = new ResizeObserver(place);
    sizes.observe(panel);
    addEventListener('scroll', place, true);
    return () => {
      sizes.disconnect();
      removeEventListener('scroll', place, true);
      if (panel.matches(':popover-open')) panel.hidePopover();
    };
  }, [anchor]);
  return (
    <div ref={ref} popover="manual" aria-hidden className="bpp-card-preview">
      {children}
    </div>
  );
}

function usePreview(preview: ReactNode | undefined) {
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  if (!preview) return { hover: {}, panel: null };
  const handlers = {
    onPointerEnter: (event: PointerEvent<HTMLElement>) => {
      if (event.pointerType === 'mouse') setAnchor(event.currentTarget);
    },
    onPointerLeave: () => setAnchor(null),
    onFocus: (event: FocusEvent<HTMLElement>) => {
      if (event.currentTarget.matches(':focus-visible'))
        setAnchor(event.currentTarget);
    },
    onBlur: () => setAnchor(null)
  };
  return {
    hover: handlers,
    panel: anchor && <Preview anchor={anchor}>{preview}</Preview>
  };
}

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
  const { hover, panel } = usePreview(card.preview);
  return (
    <>
      <Element
        {...hover}
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
        {card.enchant && (
          <span
            aria-hidden="true"
            className={`bpp-card-gem ${enchantClass(card.enchant)}`}
          />
        )}
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
      {panel}
    </>
  );
}

/** A skill: round, ringed in its tier, named only in its title and alt text. */
export function SkillTile({ card }: { card: GameCard }) {
  const Element = card.onOpen ? 'button' : 'span';
  const { hover, panel } = usePreview(card.preview);
  return (
    <>
      <Element
        {...hover}
        type={card.onOpen ? 'button' : undefined}
        onClick={card.onOpen}
        className={`bpp-skill-tile ${tierClass(card.tier)}`}
        title={[card.name, card.tierLabel].filter(Boolean).join(' · ')}
      >
        <CardArt src={card.art} alt={card.name} fallback={card.name} />
        <TierLabel label={card.tierLabel} />
      </Element>
      {panel}
    </>
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
