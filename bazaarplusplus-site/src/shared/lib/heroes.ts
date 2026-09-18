export const HEROES = [
  'Stelle',
  'Mak',
  'Jules',
  'Dooley',
  'Karnok',
  'Pygmalien',
  'Vanessa',
  'TheDragons',
] as const;

type HeroName = (typeof HEROES)[number];

export const HERO_MAPPING: Record<HeroName, { shortLabel: string; color: string }> = {
  Stelle: { shortLabel: 'STE', color: '#ffeb18' },
  Mak: { shortLabel: 'MAK', color: '#bee65b' },
  Jules: { shortLabel: 'JUL', color: '#b434ec' },
  Dooley: { shortLabel: 'DOO', color: '#e19a08' },
  Karnok: { shortLabel: 'KAR', color: '#3b889c' },
  Pygmalien: { shortLabel: 'PYG', color: '#2767c0' },
  Vanessa: { shortLabel: 'VAN', color: '#c02121' },
  TheDragons: { shortLabel: 'DRA', color: '#2dd2d0' },
};

const HERO_COLORS: Record<HeroName, string> = Object.fromEntries(
  HEROES.map((hero) => [hero, HERO_MAPPING[hero].color])
) as Record<HeroName, string>;

const HERO_SHORT_LABELS: Record<HeroName, string> = Object.fromEntries(
  HEROES.map((hero) => [hero, HERO_MAPPING[hero].shortLabel])
) as Record<HeroName, string>;

const FALLBACK_HERO_COLOR = '#394961';

function isHeroName(value: string): value is HeroName {
  return (HEROES as readonly string[]).includes(value);
}

export function getHeroColor(hero: string): string {
  return isHeroName(hero) ? HERO_COLORS[hero] : FALLBACK_HERO_COLOR;
}

export function getHeroShortLabel(hero: string): string {
  return isHeroName(hero) ? HERO_SHORT_LABELS[hero] : hero.slice(0, 3).toUpperCase();
}
