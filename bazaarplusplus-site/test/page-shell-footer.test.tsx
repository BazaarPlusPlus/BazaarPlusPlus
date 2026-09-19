import { render, screen } from '@testing-library/react';
import { describe, expect, test } from 'vitest';

import InfoPageShell from '../src/shared/components/InfoPageShell';
import StatsPageShell from '../src/shared/components/StatsPageShell';
import { createMemorySpaLocationAdapter, createSpaLocation } from '../src/app/router';

function pageLocation(href: string) {
  const memory = createMemorySpaLocationAdapter(href);
  return createSpaLocation(memory.adapter).current();
}

describe('page shell footer', () => {
  test('renders the author credit link in info pages', () => {
    render(
      <InfoPageShell
        locale="en"
        location={pageLocation('/download?lang=en')}
        title="Download BazaarPlusPlus"
      >
        <div>Download content</div>
      </InfoPageShell>
    );

    expect(screen.getByText('Made with')).toBeInTheDocument();
    expect(screen.getByRole('img', { name: 'love' })).toBeInTheDocument();
    expect(screen.getByText('by')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Xinyu YANG' })).toHaveAttribute(
      'href',
      'https://github.com/cauyxy'
    );
  });

  test('renders the author credit in English for Chinese pages', () => {
    render(
      <InfoPageShell locale="zh" location={pageLocation('/download')} title="下载 BazaarPlusPlus">
        <div>下载内容</div>
      </InfoPageShell>
    );

    expect(screen.getByText('Made with')).toBeInTheDocument();
    expect(screen.getByRole('img', { name: 'love' })).toBeInTheDocument();
    expect(screen.getByText('by')).toBeInTheDocument();
    expect(screen.queryByText('用')).not.toBeInTheDocument();
    expect(screen.queryByText('制作：')).not.toBeInTheDocument();
  });

  test('renders the author credit link in stats pages', () => {
    render(
      <StatsPageShell
        locale="en"
        location={pageLocation('/heroes?lang=en')}
        eyebrow="Analytics"
        title="Hero winrate"
        generatedAt="2026-04-18T18:57:46Z"
        filters={<div>Filters</div>}
      >
        <div>Stats content</div>
      </StatsPageShell>
    );

    expect(screen.getByText('Made with')).toBeInTheDocument();
    expect(screen.getByRole('img', { name: 'love' })).toBeInTheDocument();
    expect(screen.getByText('by')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Xinyu YANG' })).toHaveAttribute(
      'href',
      'https://github.com/cauyxy'
    );
  });
});
