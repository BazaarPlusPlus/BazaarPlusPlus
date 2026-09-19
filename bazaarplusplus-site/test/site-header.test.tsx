import { render, screen, within } from '@testing-library/react';
import { describe, expect, test } from 'vitest';

import SiteHeader from '../src/shared/components/SiteHeader';
import { createMemorySpaLocationAdapter, createSpaLocation } from '../src/app/router';

function renderHeader(href: string) {
  const memory = createMemorySpaLocationAdapter(href);
  return render(<SiteHeader location={createSpaLocation(memory.adapter).current()} />);
}

describe('SiteHeader', () => {
  test('renders localized unified nav without a live feed pill', () => {
    const { container } = renderHeader('/heroes');

    const primaryNav = screen.getByRole('navigation', { name: '主要导航' });

    expect(within(primaryNav).getByRole('link', { name: '统计' })).toHaveAttribute(
      'href',
      '/heroes'
    );
    expect(within(primaryNav).getByRole('link', { name: '教程' })).toHaveAttribute(
      'href',
      '/tutorial'
    );
    expect(within(primaryNav).getByRole('link', { name: '下载' })).toHaveAttribute(
      'href',
      '/download'
    );
    expect(within(primaryNav).getByRole('link', { name: '支持' })).toHaveAttribute(
      'href',
      '/support'
    );
    expect(screen.queryByRole('navigation', { name: '辅助导航' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: '支持者' })).not.toBeInTheDocument();

    expect(container.querySelector('source')).not.toBeInTheDocument();
    expect(container.querySelector('img[src="/bazaarplusplus-icon.webp"]')).toBeInTheDocument();
    expect(container.querySelector('img[src="/bazaarplusplus-icon.png"]')).not.toBeInTheDocument();
    expect(screen.queryByText('实时数据')).not.toBeInTheDocument();
    expect(within(primaryNav).getByRole('link', { name: '统计' })).toHaveAttribute(
      'aria-current',
      'page'
    );
  });

  test('uses english labels when explicitly selected', () => {
    renderHeader('/download?lang=en');

    expect(screen.queryByText('Live feed')).not.toBeInTheDocument();

    expect(screen.getByRole('link', { name: 'Stats' })).toHaveAttribute('href', '/heroes?lang=en');
    expect(screen.getByRole('link', { name: 'Tutorial' })).toHaveAttribute(
      'href',
      '/tutorial?lang=en'
    );
    expect(screen.getByRole('link', { name: 'Download' })).toHaveAttribute(
      'href',
      '/download?lang=en'
    );
    expect(screen.getByRole('link', { name: 'Download' })).toHaveAttribute('aria-current', 'page');
    expect(screen.getByRole('link', { name: 'Support' })).toHaveAttribute(
      'href',
      '/support?lang=en'
    );
    expect(screen.queryByRole('link', { name: 'Supporters' })).not.toBeInTheDocument();
  });

  test('language toggle highlights current locale and links to other locale preserving path + query', () => {
    renderHeader('/heroes?w=3d&s=legend&lang=en');

    const group = screen.getByRole('group', { name: 'Language' });

    const enSegment = group.querySelector('span[aria-current="true"]');
    expect(enSegment).toHaveTextContent('EN');

    const zhLink = screen.getByRole('link', { name: '中' });
    expect(zhLink).toHaveAttribute('href', '/heroes?w=3d&s=legend');
    expect(zhLink).toHaveAttribute('hrefLang', 'zh');
  });

  test('language toggle adds lang param when switching to english', () => {
    renderHeader('/heroes?w=7d');

    const enLink = screen.getByRole('link', { name: 'EN' });
    expect(enLink).toHaveAttribute('href', '/heroes?w=7d&lang=en');
    expect(enLink).toHaveAttribute('hrefLang', 'en');

    const zhSegment = screen
      .getByRole('group', { name: '语言' })
      .querySelector('span[aria-current="true"]');
    expect(zhSegment).toHaveTextContent('中');
  });

  test('marks tutorial as active in the unified nav', () => {
    renderHeader('/tutorial?lang=en');

    expect(screen.getByRole('link', { name: 'Tutorial' })).toHaveAttribute('aria-current', 'page');
  });
});
