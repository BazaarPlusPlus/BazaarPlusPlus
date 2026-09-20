import { getHeroColor, getHeroShortLabel } from '../lib/heroes';

type HeroBadgeProps = {
  hero: string;
  selected?: boolean;
};

export default function HeroBadge({ hero, selected = false }: HeroBadgeProps) {
  const color = getHeroColor(hero);
  const label = getHeroShortLabel(hero);

  return (
    <span
      data-hero-badge={hero}
      data-hero-short-label={label}
      title={hero}
      className={`group inline-flex min-w-0 items-center rounded-md border font-mono font-semibold uppercase tracking-[0.16em] transition gap-1.5 px-2 py-1 text-[0.7rem] ${
        selected
          ? 'border-[color:var(--color-accent)] bg-[color:rgba(232,185,74,0.12)] text-[color:var(--color-text-base)] shadow-[inset_0_0_0_1px_rgba(232,185,74,0.25)]'
          : 'border-[color:var(--color-border-soft)] bg-[color:rgba(15,12,8,0.7)] text-[color:var(--color-text-base)] hover:border-[color:var(--color-accent)]'
      } `}
      style={{ '--hero-color': color } as React.CSSProperties}
    >
      <span
        data-hero-color-dot={hero}
        className="h-4 w-1 shrink-0 rounded-sm"
        style={{
          backgroundColor: color,
          boxShadow: `0 0 8px ${color}40`,
        }}
        aria-hidden="true"
      />
      <span aria-hidden="true">{label}</span>
      <span className="sr-only">{hero}</span>
    </span>
  );
}
