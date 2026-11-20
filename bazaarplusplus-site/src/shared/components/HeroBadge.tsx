import type { ReactNode } from 'react';

import { getHeroColor, getHeroShortLabel } from '../lib/heroes';

type HeroBadgeProps = {
  hero: string;
  selected?: boolean;
  label?: 'short' | 'full';
  children?: ReactNode;
};

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
