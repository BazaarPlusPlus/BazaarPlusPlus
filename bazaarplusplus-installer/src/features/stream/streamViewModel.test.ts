import { describe, expect, it } from 'vitest';
import { createStreamViewModel } from './streamViewModel';
import type { StreamServiceStatus } from '../../types/backend';

const baseStatus: StreamServiceStatus = {
  running: false,
  host: '127.0.0.1',
  port: null,
  base_url: null,
  overlay_url: null,
  settings_url: null,
  last_error: null,
  started_at: null,
  active_from: null,
  active_window_offset: 0,
  db: {
    found: false,
    path: null
  },
  window: {
    total_records: 0,
    existing_before_start: 0,
    captured_since_start: 0,
    current_hero: null,
    current_start_label: null
  }
};

describe('createStreamViewModel', () => {
  it('reports a starting state while route-enter ensure is loading', () => {
    const model = createStreamViewModel({
      status: baseStatus,
      loading: true,
      action: null,
      error: null
    });

    expect(model.state).toBe('starting');
    expect(model.message).toBeNull();
    expect(model.canOpenOverlay).toBe(false);
  });

  it('exposes overlay and settings actions for a healthy session', () => {
    const model = createStreamViewModel({
      status: {
        ...baseStatus,
        running: true,
        port: 17654,
        base_url: 'http://127.0.0.1:17654',
        overlay_url: 'http://127.0.0.1:17654/overlay',
        settings_url: 'http://127.0.0.1:17654/settings'
      },
      loading: false,
      action: null,
      error: null
    });

    expect(model.state).toBe('running');
    expect(model.obsUrl).toBe('http://127.0.0.1:17654/overlay');
    expect(model.canOpenOverlay).toBe(true);
    expect(model.canOpenSettings).toBe(true);
  });

  it('surfaces backend errors above stale status details', () => {
    const model = createStreamViewModel({
      status: { ...baseStatus, last_error: 'Port is occupied' },
      loading: false,
      action: null,
      error: 'Port is occupied'
    });

    expect(model.state).toBe('error');
    expect(model.message).toBe('Port is occupied');
    expect(model.canOpenOverlay).toBe(false);
  });
});
