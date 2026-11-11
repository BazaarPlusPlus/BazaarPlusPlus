// @vitest-environment-options {"url":"https://example.test/heroes?lang=en&w=3d&s=legend"}

import { act, createElement } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, describe, expect, test, vi } from 'vite-plus/test';

import { createSpaLocation } from '../src/app/router';
import type { AnalysisScope } from '../src/features/heroes/hero-analysis';
import type { HeroMetricsDataset } from '../src/features/heroes/hero-metrics-dataset';
import HeroOverviewDashboard from '../src/features/heroes/HeroOverviewDashboard';

const dataset: HeroMetricsDataset = {
  generatedAt: '2026-06-08T09:00:00Z',
  window: { start: '2026-06-06', end: '2026-06-06', days: 1 },
  days: [
    {
      day: '2026-06-06',
      rows: [
        {
          hero: 'Vanessa',
          segment: 'legend',
          runs: { completed: 4, scored: 4, ten_win: 1 },
          outcomes: { perfect: 0, gold: 1, silver: 0, bronze: 0 },
          ten_win_days: { known_count: 0, sum_days: 0 },
          matchups: []
        }
      ]
    }
  ],
  coverage: {
    requestedDates: ['2026-06-06'],
    usableDates: ['2026-06-06'],
    failedDates: []
  }
};

let root: Root | null = null;

afterEach(() => {
  act(() => root?.unmount());
  root = null;
  document.body.innerHTML = '';
});

function render(onScopeChange: (scope: AnalysisScope) => void) {
  (
    globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }
  ).IS_REACT_ACT_ENVIRONMENT = true;
  const container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
  act(() =>
    root!.render(
      createElement(HeroOverviewDashboard, {
        location: createSpaLocation().current(),
        dataset,
        onScopeChange
      })
    )
  );
  return container;
}

function radioGroup(container: HTMLElement, name: string) {
  const groups = container.querySelectorAll<HTMLElement>(
    `[aria-label="${name}"]`
  );
  expect(groups).toHaveLength(1);
  expect(groups[0].getAttribute('role')).toBe('radiogroup');
  return groups[0];
}

function checkedLabel(group: HTMLElement) {
  return group.querySelector('input:checked')?.closest('label')?.textContent;
}

describe('Hero Analysis scope filters', () => {
  test('name each filter once and check the URL scope', () => {
    const container = render(() => undefined);

    expect(checkedLabel(radioGroup(container, 'Window'))).toBe('3D');
    expect(checkedLabel(radioGroup(container, 'Segment'))).toBe('Legend');
  });

  test('a radio change reports the new scope and keeps the other filter', () => {
    const onScopeChange = vi.fn();
    const container = render(onScopeChange);

    const window = radioGroup(container, 'Window');
    act(() =>
      window.querySelector<HTMLInputElement>('input[value="1d"]')!.click()
    );
    expect(onScopeChange).toHaveBeenLastCalledWith({
      window: '1d',
      segment: 'legend'
    });

    const segment = radioGroup(container, 'Segment');
    act(() =>
      segment.querySelector<HTMLInputElement>('input[value="all"]')!.click()
    );
    expect(onScopeChange).toHaveBeenLastCalledWith({
      window: '3d',
      segment: 'all'
    });
  });
});
