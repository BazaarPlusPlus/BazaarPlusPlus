// @vitest-environment jsdom

// Isolated on purpose: the page-level refresh on visibility and focus is
// anchored by pages/History.test.tsx. These cases need the native window API,
// which the page tests do not run with, or observe events after disposal.
// - Native focus registration fails and the page never refreshes on focus.
// - A blur or a hidden-to-hidden change triggers a refresh.
// - A listener keeps firing after disposal, or a native registration that
//   finishes after disposal is never released.

import type { Event as TauriEvent } from '@tauri-apps/api/event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { hasTauriRuntime } from '../../api/runtime';
import { observeHistoryWindowResume } from './historyWindowResume';

const windowApi = vi.hoisted(() => ({ onFocusChanged: vi.fn() }));
vi.mock('@tauri-apps/api/window', () => ({
  getCurrentWindow: () => windowApi
}));
vi.mock('../../api/runtime', () => ({ hasTauriRuntime: vi.fn(() => false) }));
afterEach(() => {
  vi.restoreAllMocks();
  vi.mocked(hasTauriRuntime).mockReturnValue(false);
});

describe('History window resume', () => {
  it('falls back to browser focus if native registration fails', async () => {
    vi.mocked(hasTauriRuntime).mockReturnValue(true);
    windowApi.onFocusChanged.mockRejectedValue(
      new Error('listener unavailable')
    );
    const resumed = vi.fn();
    const dispose = observeHistoryWindowResume(resumed);
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
    const dispose = observeHistoryWindowResume(resumed);
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
    const dispose = observeHistoryWindowResume(resumed);
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
