import { test, expect } from 'vitest';

import { createStreamPageState } from './state.ts';

test('createStreamPageState enables preview actions only when service is running with a URL', () => {
  const state = createStreamPageState({
    running: true,
    host: '127.0.0.1',
    port: 17654,
    overlay_url: 'http://127.0.0.1:17654/overlay',
    using_fallback_port: false,
    last_error: null,
    started_at: '2026-04-11T21:00:00+08:00',
    active_from: '2026-04-11T21:00:00+08:00',
    active_window_offset: 0
  });

  expect(state.canCopyUrl).toBe(true);
  expect(state.canOpenPreview).toBe(true);
});

test('createStreamPageState disables preview actions when service is stopped', () => {
  const state = createStreamPageState({
    running: false,
    host: '127.0.0.1',
    port: null,
    overlay_url: null,
    using_fallback_port: false,
    last_error: null,
    started_at: null,
    active_from: null,
    active_window_offset: 0
  });

  expect(state.canCopyUrl).toBe(false);
  expect(state.canOpenPreview).toBe(false);
});
