import { describe, expect, test } from 'vitest';

import { HEROES, HERO_MAPPING, getHeroColor, getHeroShortLabel } from '../src/shared/lib/heroes';

describe('hero mapping', () => {
  test('keeps the canonical hero short labels and colors together', () => {
    expect(HEROES).toEqual([
      'Stelle',
      'Mak',
      'Jules',
      'Dooley',
      'Karnok',
      'Pygmalien',
      'Vanessa',
      'TheDragons',
    ]);
    expect(HERO_MAPPING).toEqual({
      Stelle: { shortLabel: 'STE', color: '#ffeb18' },
      Mak: { shortLabel: 'MAK', color: '#bee65b' },
      Jules: { shortLabel: 'JUL', color: '#b434ec' },
      Dooley: { shortLabel: 'DOO', color: '#e19a08' },
      Karnok: { shortLabel: 'KAR', color: '#3b889c' },
      Pygmalien: { shortLabel: 'PYG', color: '#2767c0' },
      Vanessa: { shortLabel: 'VAN', color: '#c02121' },
      TheDragons: { shortLabel: 'DRA', color: '#2dd2d0' },
    });
    expect(getHeroShortLabel('Karnok')).toBe('KAR');
    expect(getHeroColor('Karnok')).toBe('#3b889c');
  });
});
