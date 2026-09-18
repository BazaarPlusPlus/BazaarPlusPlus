import { render, screen } from '@testing-library/react';
import { afterEach, describe, expect, test, vi } from 'vitest';

const heroRouteModule = vi.hoisted(() => ({
  loadSpy: vi.fn(),
}));

vi.mock('../src/app/route-pages', () => {
  heroRouteModule.loadSpy();
  return {
    HeroOverviewPage: () => null,
  };
});

describe('App route loading', () => {
  afterEach(() => {
    vi.clearAllMocks();
  });

  test('does not load the hero dashboard route module for info pages', async () => {
    window.history.replaceState({}, '', '/tutorial?lang=en');

    const { default: App } = await import('../src/app/App');
    render(<App />);

    expect(heroRouteModule.loadSpy).not.toHaveBeenCalled();
    expect(
      screen.getByRole('heading', { level: 1, name: 'BazaarPlusPlus Official Guide' })
    ).toBeInTheDocument();
  });
});
