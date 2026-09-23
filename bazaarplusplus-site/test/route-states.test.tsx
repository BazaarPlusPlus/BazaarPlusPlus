import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, within } from '@testing-library/react';
import { describe, expect, test, vi } from 'vitest';

import { HeroOverviewPage } from '../src/app/route-pages';
import { createMemorySpaLocationAdapter, createSpaLocation } from '../src/app/router';
import { NotFoundScreen } from '../src/app/screens';
import type { HeroMetricsTransport } from '../src/features/heroes/hero-metrics-dataset';
import { HEROES } from '../src/shared/lib/heroes';

function locationFor(href: string) {
  return createSpaLocation(createMemorySpaLocationAdapter(href).adapter).current();
}

function makeSnapshot() {
  const day = '2026-06-07';
  return {
    schema_version: 1,
    kind: 'hero_metrics',
    generated_at: '2026-06-08T09:00:00Z',
    window: { start: day, end: day, days: 1 },
    days: [
      {
        day,
        rows: HEROES.flatMap((hero) =>
          (['legend', 'non_legend'] as const).map((segment) => ({
            hero,
            segment,
            runs: { completed: 8, scored: 8, ten_win: 2 },
            outcomes: { perfect: 1, gold: 1, silver: 2, bronze: 2 },
            ten_win_days: { known_count: 2, sum_days: 22 },
            matchups: [],
          }))
        ),
      },
    ],
  };
}

function renderHeroPage(transport: HeroMetricsTransport, href = '/heroes?lang=en') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } });
  render(
    <QueryClientProvider client={client}>
      <HeroOverviewPage
        transport={transport}
        location={locationFor(href)}
        onScopeChange={vi.fn()}
      />
    </QueryClientProvider>
  );
}

describe('route states', () => {
  test('Hero Analysis failure shows translated copy in the page chrome and retries the query', async () => {
    const load = vi
      .fn<HeroMetricsTransport['load']>()
      .mockRejectedValueOnce(new Error('socket hang up'))
      .mockResolvedValueOnce(makeSnapshot());
    renderHeroPage({ load });

    expect(
      await screen.findByRole('heading', { level: 2, name: 'Stats are temporarily unavailable' })
    ).toBeInTheDocument();
    expect(
      screen.getByText('The hero stats could not be loaded. Try again in a moment.')
    ).toBeInTheDocument();
    expect(screen.queryByText(/socket hang up/)).not.toBeInTheDocument();
    expect(screen.getByRole('navigation', { name: 'Primary' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Xinyu YANG' })).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Try again' }));

    expect(await screen.findByTestId('ranking-panel')).toBeInTheDocument();
    expect(load).toHaveBeenCalledTimes(2);
  });

  test('localizes the retry state in Chinese', async () => {
    renderHeroPage({ load: vi.fn().mockRejectedValue(new Error('boom')) }, '/heroes');

    expect(await screen.findByRole('button', { name: '重试' })).toBeInTheDocument();
    expect(screen.getByText('暂时无法读取英雄统计数据，请稍后重试。')).toBeInTheDocument();
    expect(screen.queryByText('boom')).not.toBeInTheDocument();
  });

  test('not-found renders inside the header and footer chrome', () => {
    render(<NotFoundScreen location={locationFor('/missing?lang=en')} />);

    expect(screen.getByRole('heading', { level: 1, name: 'Page not found' })).toBeInTheDocument();
    const nav = screen.getByRole('navigation', { name: 'Primary' });
    expect(within(nav).getByRole('link', { name: 'Stats' })).toHaveAttribute(
      'href',
      '/heroes?lang=en'
    );
    expect(screen.getByRole('link', { name: '← Back to hero overview' })).toHaveAttribute(
      'href',
      '/heroes?lang=en'
    );
    expect(screen.getByRole('link', { name: 'Xinyu YANG' })).toBeInTheDocument();
  });
});
