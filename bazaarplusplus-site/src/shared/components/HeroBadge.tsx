import type { ReactNode } from 'react';

import { getHeroColor, getHeroShortLabel } from '../lib/heroes';

type HeroBadgeProps = {
  hero: string;
  selected?: boolean;
  /** `short` shows the three-letter code and keeps the full name for assistive tech. */
  label?: 'short' | 'full';
  /** Trailing value, such as a win rate or the active window. */
  children?: ReactNode;
};

/** The one hero chip: a hero-colored dot, the hero's name, and an optional
 *  trailing value. Its look is `.bpp-hero-badge` in design/components/bundle.css. */
export default function HeroBadge({
  hero,
  selected = false,
  label = 'short',
  children
}: HeroBadgeProps) {
  return (
    <span
      data-hero-badge={hero}
      title={hero}
      className={selected ? 'bpp-hero-badge is-selected' : 'bpp-hero-badge'}
    >
      <span
        className="bpp-hero-badge-dot"
        style={{ backgroundColor: getHeroColor(hero) }}
        aria-hidden="true"
      />
      {label === 'short' ? (
        <>
          <span aria-hidden="true">{getHeroShortLabel(hero)}</span>
          <span className="sr-only">{hero}</span>
        </>
      ) : (
        <span className="min-w-0 truncate">{hero}</span>
      )}
      {children != null ? (
        <span className="bpp-hero-badge-value">{children}</span>
      ) : null}
    </span>
  );
}
