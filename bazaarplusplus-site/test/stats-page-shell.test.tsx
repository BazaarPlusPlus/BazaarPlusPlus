import { render, screen, within } from '@testing-library/react';
import { describe, expect, test } from 'vitest';

import StatsPageShell from '../src/shared/components/StatsPageShell';
import { createMemorySpaLocationAdapter, createSpaLocation } from '../src/app/router';

describe('StatsPageShell', () => {
  test('renders shared navigation and highlights the active section', () => {
    const memory = createMemorySpaLocationAdapter('/heroes');
    render(
      <StatsPageShell
        locale="zh"
        location={createSpaLocation(memory.adapter).current()}
        eyebrow="BazaarPlusPlus analytics"
        title="Hero winrate"
        generatedAt="2026-04-18T18:57:46Z"
        filters={<div>Filters slot</div>}
      >
        <div>Table slot</div>
      </StatsPageShell>
    );

    const heroesLink = screen.getByRole('link', { name: '统计' });
    expect(heroesLink).toHaveAttribute('href', '/heroes');
    expect(
      within(screen.getByRole('navigation', { name: '主要导航' })).queryByText('Analytics')
    ).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Archetypes' })).not.toBeInTheDocument();
    expect(heroesLink.className).toContain('text-[color:var(--color-accent-bright)]');
    expect(screen.queryByText('实时数据')).not.toBeInTheDocument();
    expect(screen.getByText('BazaarPlusPlus analytics')).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 1, name: 'Hero winrate' }).className).not.toContain(
      'sr-only'
    );
    expect(screen.getByText('Filters slot')).toBeInTheDocument();
    expect(screen.getByText('Table slot')).toBeInTheDocument();
  });
});
