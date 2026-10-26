// @vitest-environment jsdom

// Isolated on purpose: the History page's refresh on visibility and focus is
// anchored by pages/History.test.tsx. These cases need the native window and
// event APIs, which the page tests do not run with, or observe events after
// disposal.
// - Native focus registration fails and the page never refreshes on focus.
// - A blur or a hidden-to-hidden change triggers a refresh.
// - The native hide-to-tray event, or a hidden page, does not report hidden.
// - A listener keeps firing after disposal, or a native registration that
//   finishes after disposal is never released.

import type { Event as TauriEvent } from '@tauri-apps/api/event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { hasTauriRuntime } from '../../api/runtime';
import {
  MAIN_WINDOW_HIDDEN_EVENT,
  observeWindowHidden,
  observeWindowResume
} from './windowActivity';

const windowApi = vi.hoisted(() => ({ onFocusChanged: vi.fn() }));
vi.mock('@tauri-apps/api/window', () => ({
  getCurrentWindow: () => windowApi
}));
const eventApi = vi.hoisted(() => ({ listen: vi.fn() }));
vi.mock('@tauri-apps/api/event', () => eventApi);
vi.mock('../../api/runtime', () => ({ hasTauriRuntime: vi.fn(() => false) }));
afterEach(() => {
  vi.restoreAllMocks();
  vi.mocked(hasTauriRuntime).mockReturnValue(false);
});

describe('window resume', () => {
  it('falls back to browser focus if native registration fails', async () => {
    vi.mocked(hasTauriRuntime).mockReturnValue(true);
    windowApi.onFocusChanged.mockRejectedValue(
      new Error('listener unavailable')
    );
    const resumed = vi.fn();
    const dispose = observeWindowResume(resumed);
    await Promise.resolve();
    await Promise.resolve();
    window.dispatchEvent(new Event('focus'));
    expect(resumed).toHaveBeenCalledOnce();
    dispose();
    window.dispatchEvent(new Event('focus'));
    expect(resumed).toHaveBeenCalledOnce();
  });

  it('refreshes on visible/focus events and stops after disposal', () => {
    const resumed = vi.fn();
    const dispose = observeWindowResume(resumed);
    const visibility = vi.spyOn(document, 'visibilityState', 'get');
    visibility.mockReturnValue('hidden');
    document.dispatchEvent(new Event('visibilitychange'));
    expect(resumed).not.toHaveBeenCalled();
    visibility.mockReturnValue('visible');
    document.dispatchEvent(new Event('visibilitychange'));
    window.dispatchEvent(new Event('focus'));
    expect(resumed).toHaveBeenCalledTimes(2);
    dispose();
    document.dispatchEvent(new Event('visibilitychange'));
    window.dispatchEvent(new Event('focus'));
    expect(resumed).toHaveBeenCalledTimes(2);
  });

  it('uses native focus and releases a listener whose registration finishes after disposal', async () => {
    vi.mocked(hasTauriRuntime).mockReturnValue(true);
    let focused!: (event: TauriEvent<boolean>) => void;
    let registered!: (stop: () => void) => void;
    windowApi.onFocusChanged.mockImplementation((callback: typeof focused) => {
      focused = callback;
      return new Promise<() => void>((resolve) => {
        registered = resolve;
      });
    });
    const resumed = vi.fn();
    const dispose = observeWindowResume(resumed);
    focused({ event: 'tauri://blur', id: 1, payload: false });
    expect(resumed).not.toHaveBeenCalled();
    focused({ event: 'tauri://focus', id: 1, payload: true });
    expect(resumed).toHaveBeenCalledOnce();
    dispose();
    focused({ event: 'tauri://focus', id: 1, payload: true });
    const stopped = vi.fn();
    registered(stopped);
    await Promise.resolve();
    expect(stopped).toHaveBeenCalledOnce();
    expect(resumed).toHaveBeenCalledOnce();
  });
});

describe('window hidden', () => {
  it('reports the native hide-to-tray event and a hidden page until disposal', async () => {
    vi.mocked(hasTauriRuntime).mockReturnValue(true);
    let announce!: () => void;
    const stop = vi.fn();
    eventApi.listen.mockImplementation(
      (event: string, callback: () => void) => {
        expect(event).toBe(MAIN_WINDOW_HIDDEN_EVENT);
        announce = callback;
        return Promise.resolve(stop);
      }
    );
    const hidden = vi.fn();
    const dispose = observeWindowHidden(hidden);
    await Promise.resolve();
    announce();
    expect(hidden).toHaveBeenCalledOnce();

    const visibility = vi.spyOn(document, 'visibilityState', 'get');
    visibility.mockReturnValue('visible');
    document.dispatchEvent(new Event('visibilitychange'));
    expect(hidden).toHaveBeenCalledOnce();
    visibility.mockReturnValue('hidden');
    document.dispatchEvent(new Event('visibilitychange'));
    expect(hidden).toHaveBeenCalledTimes(2);

    dispose();
    expect(stop).toHaveBeenCalledOnce();
    announce();
    document.dispatchEvent(new Event('visibilitychange'));
    expect(hidden).toHaveBeenCalledTimes(2);
  });

  it('releases a native listener whose registration finishes after disposal', async () => {
    vi.mocked(hasTauriRuntime).mockReturnValue(true);
    let registered!: (stop: () => void) => void;
    eventApi.listen.mockImplementation(
      () =>
        new Promise<() => void>((resolve) => {
          registered = resolve;
        })
    );
    const dispose = observeWindowHidden(vi.fn());
    dispose();
    const stopped = vi.fn();
    registered(stopped);
    await Promise.resolve();
    expect(stopped).toHaveBeenCalledOnce();
  });
});
