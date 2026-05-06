export const HERO_KEY_BY_NAME: Record<string, string> = {
  Vanessa: 'van',
  Pygmalien: 'pyg',
  Dooley: 'doo',
  Mak: 'mak',
  Jules: 'jul',
  Karnok: 'kar',
  Stelle: 'ste'
};

export const UNKNOWN_HERO_KEY = 'unk';

export function resolveHeroKey(heroName: string | null | undefined): string {
  const normalized = heroName?.trim() || '';
  return HERO_KEY_BY_NAME[normalized] || UNKNOWN_HERO_KEY;
}
