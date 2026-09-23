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

/** The one hero chip: a hero-colored dot, the hero's name, and an optional trailing value. */
export default function HeroBadge({
  hero,
  selected = false,
  label = 'short',
  children,
}: HeroBadgeProps) {
  const color = getHeroColor(hero);
  const shortLabel = getHeroShortLabel(hero);

  return (
    <span
      data-hero-badge={hero}
      data-hero-short-label={shortLabel}
      title={hero}
      className={`inline-flex h-[22px] min-w-0 items-center gap-1.5 rounded-full border px-2 text-xs font-semibold whitespace-nowrap text-text-1 transition-colors duration-(--t-fast) ${
        selected ? 'border-accent-line bg-accent-subtle' : 'border-transparent bg-hover'
      }`}
    >
      <span
        data-hero-color-dot={hero}
        className="size-1.5 shrink-0 rounded-full"
        style={{ backgroundColor: color }}
        aria-hidden="true"
      />
      {label === 'short' ? (
        <>
          <span aria-hidden="true">{shortLabel}</span>
          <span className="sr-only">{hero}</span>
        </>
      ) : (
        <span className="min-w-0 truncate">{hero}</span>
      )}
      {children != null ? (
        <span className={`font-medium tabular-nums ${selected ? 'text-accent' : 'text-text-2'}`}>
          {children}
        </span>
      ) : null}
    </span>
  );
}
