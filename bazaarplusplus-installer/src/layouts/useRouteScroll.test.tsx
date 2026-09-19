// @vitest-environment jsdom

import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import {
  MemoryRouter,
  useLocation,
  useNavigate,
  type NavigateFunction
} from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { useRouteScroll } from './useRouteScroll';

let navigate: NavigateFunction;
let root: Root;
let container: HTMLDivElement;
let resize: () => void;
let maximum = 3000;
let offset = 0;

function Shell() {
  navigate = useNavigate();
  const location = useLocation();
  const ref = useRouteScroll();
  return (
    <main ref={ref}>
      <div>{location.pathname + location.search}</div>
    </main>
  );
}

beforeEach(async () => {
  maximum = 3000;
  offset = 0;
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  vi.stubGlobal(
    'ResizeObserver',
    class {
      constructor(callback: () => void) {
        resize = callback;
      }
      observe() {}
      disconnect() {}
    }
  );
  vi.spyOn(HTMLElement.prototype, 'scrollTop', 'get').mockImplementation(
    () => offset
  );
  vi.spyOn(HTMLElement.prototype, 'scrollTop', 'set').mockImplementation(
    (value) => {
      offset = Math.min(value, maximum);
    }
  );
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
  await act(async () => {
    root.render(
      <MemoryRouter initialEntries={['/history?page=3']}>
        <Shell />
      </MemoryRouter>
    );
  });
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

function scrollTo(value: number) {
  container.querySelector('main')!.scrollTop = value;
  container.querySelector('main')!.dispatchEvent(new Event('scroll'));
}

describe('route scrolling', () => {
  it('opens a main navigation destination at the top', async () => {
    scrollTo(450);
    await act(async () => navigate('/stream'));
    expect(offset).toBe(0);
    scrollTo(110);
    await act(async () => navigate('/about'));
    expect(offset).toBe(0);
  });

  it('restores the matching history page after its delayed content arrives', async () => {
    scrollTo(1450);
    await act(async () => navigate('/history/run-123'));
    expect(offset).toBe(0);
    maximum = 0;
    await act(async () => navigate('/history?page=3'));
    expect(offset).toBe(0);
    maximum = 3000;
    resize();
    expect(offset).toBe(1450);
  });

  it('restores pagination on browser Back but starts a new page at the top', async () => {
    scrollTo(1450);
    await act(async () => navigate('/history?page=4'));
    expect(offset).toBe(0);
    scrollTo(900);
    await act(async () => navigate(-1));
    expect(container.textContent).toBe('/history?page=3');
    expect(offset).toBe(1450);
  });

  it('forgets an older offset when the list is reopened through main navigation', async () => {
    scrollTo(1450);
    await act(async () => navigate('/about'));
    await act(async () => navigate('/history?page=3'));
    expect(offset).toBe(0);
    await act(async () => navigate('/history/run-123'));
    await act(async () => navigate('/history?page=3'));
    expect(offset).toBe(0);
  });

  it('lets user input cancel a pending restoration', async () => {
    scrollTo(1450);
    await act(async () => navigate('/history/run-123'));
    maximum = 0;
    await act(async () => navigate(-1));
    container.querySelector('main')!.dispatchEvent(new Event('wheel'));
    maximum = 3000;
    scrollTo(100);
    expect(offset).toBe(100);
    await act(async () => navigate('/about'));
    await act(async () => navigate(-1));
    expect(offset).toBe(100);
  });
});
