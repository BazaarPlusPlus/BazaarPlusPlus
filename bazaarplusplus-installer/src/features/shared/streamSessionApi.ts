import { invokeCommand } from '../../api/tauri';
import { hasTauriRuntime } from '../../api/runtime';
import type { StreamServiceStatus } from '../../types/backend';

export const idleStreamStatus: StreamServiceStatus = {
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

export async function ensureStreamSession() {
  if (!hasTauriRuntime()) {
    return idleStreamStatus;
  }

  // This intentionally starts the local HTTP service when it is not running.
  return invokeCommand('ensure_stream_session', {});
}

export async function getStreamStatus() {
  if (!hasTauriRuntime()) {
    return idleStreamStatus;
  }

  return invokeCommand('get_stream_status');
}
