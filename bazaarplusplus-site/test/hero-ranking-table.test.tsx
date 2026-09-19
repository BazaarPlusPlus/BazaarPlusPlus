import { fireEvent, render, screen, within } from '@testing-library/react';
import { describe, expect, test, vi } from 'vitest';

import type { HeroRanking } from '../src/features/heroes/hero-analysis';
import HeroRankingTable from '../src/features/heroes/HeroRankingTable';

function makeRanking(
  hero: string,
  tenWinRate: number | null,
  avgRunDays10w: number | null
): HeroRanking {
  return {
    hero,
    runsCompleted: 100,
    scoredRuns: 100,
    runShare: 1 / 3,
    tenWinCount: tenWinRate == null ? 0 : tenWinRate * 100,
    tenWinRate,
    avgRunDays10w,
    perfectRate: tenWinRate,
    goldRate: tenWinRate,
    silverRate: 0.1,
    bronzeRate: 0.1,
    misfortuneRate: 0.1,
  };
}

function heroOrder(): Array<string | null> {
  return Array.from(
    screen.getByTestId('hero-ranking-table').querySelectorAll('tbody [data-hero-badge]')
  ).map((badge) => badge.getAttribute('data-hero-badge'));
}

describe('HeroRankingTable interface', () => {
  test('keeps real column knowledge and null-safe sorting in one module', () => {
    render(
      <HeroRankingTable
        locale="en"
        rows={[
          makeRanking('Vanessa', 0.4, 10),
          makeRanking('Stelle', null, null),
          makeRanking('Mak', 0.6, 12),
        ]}
        focusedHero="Mak"
        onFocusHero={vi.fn()}
      />
    );

    const table = screen.getByTestId('hero-ranking-table');
    expect(table.querySelectorAll('col')).toHaveLength(11);
    expect(heroOrder()).toEqual(['Mak', 'Vanessa', 'Stelle']);
    expect(within(table).getByRole('columnheader', { name: /10W rate/ })).toHaveAttribute(
      'aria-sort',
      'descending'
    );

    fireEvent.click(within(table).getByRole('button', { name: 'Hero' }));
    expect(heroOrder()).toEqual(['Mak', 'Stelle', 'Vanessa']);
    fireEvent.click(within(table).getByRole('button', { name: 'Hero' }));
    expect(heroOrder()).toEqual(['Vanessa', 'Stelle', 'Mak']);

    fireEvent.click(within(table).getByRole('button', { name: /Avg days/ }));
    expect(heroOrder()).toEqual(['Vanessa', 'Mak', 'Stelle']);
    fireEvent.click(within(table).getByRole('button', { name: /Avg days/ }));
    expect(heroOrder()).toEqual(['Mak', 'Vanessa', 'Stelle']);
  });

  test('reports Hero focus through the same interface', () => {
    const onFocusHero = vi.fn();
    render(
      <HeroRankingTable
        locale="en"
        rows={[makeRanking('Vanessa', 0.4, 10)]}
        focusedHero={null}
        onFocusHero={onFocusHero}
      />
    );

    fireEvent.click(screen.getByRole('button', { name: 'Vanessa' }));
    expect(onFocusHero).toHaveBeenCalledWith('Vanessa');
  });
});
