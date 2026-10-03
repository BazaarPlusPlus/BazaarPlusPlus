import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, describe, expect, test, vi } from 'vitest';

import { HeroOverviewPage } from '../src/app/route-pages';
import { NotFoundScreen } from '../src/app/screens';
import { HEROES } from '../src/shared/lib/heroes';
import { locationAt } from './location';

function jsonResponse(payload: unknown) {
  return new Response(JSON.stringify(payload), {
    status: 200,
    headers: { 'content-type': 'application/json' },
  });
}

// A 404 is not retried, so the error state appears without React Query's backoff.
function notFoundResponse() {
  return new Response('missing', { status: 404 });
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

function renderHeroPage(href = '/heroes?lang=en') {
  const client = new QueryClient({ defaultOptions: { queries: { gcTime: 0 } } });
  render(
    <QueryClientProvider client={client}>
      <HeroOverviewPage location={locationAt(href)} onScopeChange={vi.fn()} />
    </QueryClientProvider>
  );
}

describe('route states', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  test('Hero Analysis failure shows translated copy in the page chrome and retries the query', async () => {
    const fetchStub = vi
      .fn<typeof fetch>()
      .mockResolvedValueOnce(notFoundResponse())
      .mockResolvedValueOnce(jsonResponse(makeSnapshot()));
    vi.stubGlobal('fetch', fetchStub);
    renderHeroPage();

    expect(
      await screen.findByRole('heading', { level: 2, name: 'Stats are temporarily unavailable' })
    ).toBeInTheDocument();
    expect(
      screen.getByText('The hero stats could not be loaded. Try again in a moment.')
    ).toBeInTheDocument();
    expect(screen.queryByText(/404/)).not.toBeInTheDocument();
    expect(screen.getByRole('navigation', { name: 'Primary' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Xinyu YANG' })).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Try again' }));

    expect(await screen.findByTestId('ranking-panel')).toBeInTheDocument();
    expect(fetchStub).toHaveBeenCalledTimes(2);
  });

  test('localizes the retry state in Chinese', async () => {
    vi.stubGlobal('fetch', vi.fn<typeof fetch>().mockResolvedValue(notFoundResponse()));
    renderHeroPage('/heroes');

    expect(await screen.findByRole('button', { name: '重试' })).toBeInTheDocument();
    expect(screen.getByText('暂时无法读取英雄统计数据，请稍后重试。')).toBeInTheDocument();
    expect(screen.queryByText(/404/)).not.toBeInTheDocument();
  });

  test('not-found renders inside the header and footer chrome', () => {
    render(<NotFoundScreen location={locationAt('/missing?lang=en')} />);

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
