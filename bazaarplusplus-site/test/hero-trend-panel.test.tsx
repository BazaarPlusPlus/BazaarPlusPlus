import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, test, vi } from 'vitest';

import type { HeroAnalysis } from '../src/features/heroes/hero-analysis';
import HeroTrendPanel from '../src/features/heroes/HeroTrendPanel';

const trend: HeroAnalysis['trend'] = {
  dayAxis: ['2026-08-10', '2026-08-11', '2026-08-12'],
  series: [
    {
      hero: 'Vanessa',
      color: '#c02121',
      points: [
        { day: '2026-08-10', winRate: 0.4 },
        { day: '2026-08-12', winRate: 0.6 },
      ],
      segments: [[{ day: '2026-08-10', winRate: 0.4 }], [{ day: '2026-08-12', winRate: 0.6 }]],
      firstWinRate: 0.4,
      latestWinRate: 0.6,
      nullPointCount: 1,
    },
    {
      hero: 'Mak',
      color: '#bee65b',
      points: [
        { day: '2026-08-10', winRate: 0.55 },
        { day: '2026-08-11', winRate: 0.5 },
        { day: '2026-08-12', winRate: 0.45 },
      ],
      segments: [
        [
          { day: '2026-08-10', winRate: 0.55 },
          { day: '2026-08-11', winRate: 0.5 },
          { day: '2026-08-12', winRate: 0.45 },
        ],
      ],
      firstWinRate: 0.55,
      latestWinRate: 0.45,
      nullPointCount: 0,
    },
  ],
};

describe('HeroTrendPanel interface', () => {
  test('renders gaps and focused areas from the supplied Hero Analysis trend', () => {
    const { container } = render(
      <HeroTrendPanel locale="en" trend={trend} focusedHero="Vanessa" onFocusHero={vi.fn()} />
    );

    const vanessa = container.querySelector('[data-hero="Vanessa"]')!;
    expect(vanessa.querySelectorAll('polyline:not([stroke="transparent"])')).toHaveLength(2);
    expect(screen.getAllByTestId('focused-trend-area')).toHaveLength(2);
    expect(screen.getByText('Some days have no calculable trend point.')).toBeInTheDocument();
  });

  test('owns pointer, keyboard, touch, and tooltip geometry behavior', () => {
    const onFocusHero = vi.fn();
    const { container } = render(
      <HeroTrendPanel locale="en" trend={trend} focusedHero="Vanessa" onFocusHero={onFocusHero} />
    );

    fireEvent.focus(screen.getByLabelText('Vanessa Aug 10 40.0%'));
    expect(onFocusHero).toHaveBeenLastCalledWith('Vanessa');
    expect(screen.getByTestId('trend-point-tooltip')).toHaveTextContent('Aug 10');

    const tooltipBox = screen.getByTestId('trend-tooltip-box');
    expect(Number(tooltipBox.getAttribute('x'))).toBeGreaterThanOrEqual(56);
    expect(Number(tooltipBox.getAttribute('y'))).toBeGreaterThanOrEqual(28);

    fireEvent.touchStart(
      container.querySelector('[data-hero="Mak"] polyline[stroke="transparent"]')!,
      { touches: [{ clientX: 0 }] }
    );
    expect(onFocusHero).toHaveBeenLastCalledWith('Mak');
    expect(screen.getByTestId('trend-point-tooltip')).toHaveTextContent('Aug 12');
  });
});
