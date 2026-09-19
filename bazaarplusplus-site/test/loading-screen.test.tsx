import { render, screen } from '@testing-library/react';
import { describe, expect, test } from 'vitest';

import { LoadingScreen } from '../src/app/screens';
import { createMemorySpaLocationAdapter, createSpaLocation } from '../src/app/router';

function loadingLocation(url: string) {
  const memory = createMemorySpaLocationAdapter(url);
  return createSpaLocation(memory.adapter).current();
}

describe('LoadingScreen', () => {
  test('shows a stable single-snapshot loading state without file counters or failure copy', () => {
    render(<LoadingScreen location={loadingLocation('/heroes')} />);

    expect(screen.getByRole('heading', { level: 1, name: '英雄统计' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 2, name: '正在准备英雄统计' })).toBeInTheDocument();
    expect(screen.getByText('正在读取最新数据快照，请稍候。')).toBeInTheDocument();
    expect(screen.getByRole('progressbar', { name: '正在加载英雄统计' })).not.toHaveAttribute(
      'aria-valuenow'
    );
    expect(screen.queryByText(/heroes\/latest\.json/)).not.toBeInTheDocument();
    expect(screen.queryByText(/\d+\s*\/\s*\d+/)).not.toBeInTheDocument();
    expect(screen.queryByText(/加载失败/)).not.toBeInTheDocument();
  });

  test('localizes the stable loading state in English', () => {
    render(<LoadingScreen location={loadingLocation('/heroes?lang=en')} />);

    expect(
      screen.getByRole('heading', { level: 2, name: 'Preparing hero stats' })
    ).toBeInTheDocument();
    expect(
      screen.getByText('Reading the latest data snapshot. This should only take a moment.')
    ).toBeInTheDocument();
    expect(screen.getByRole('progressbar', { name: 'Loading hero stats' })).toBeInTheDocument();
  });
});
