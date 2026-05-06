import { call } from '../bridge/commands.ts';
import { hasTauriRuntime } from '$lib/installer/runtime';
import { buildStreamCommandArgs } from '$lib/stream/command-args';
import type {
  StreamDbPathInfo,
  StreamOverlayCropSettings,
  StreamOverlayCropSettingsPayload,
  StreamOverlayDisplayMode,
  StreamRecordSummary,
  StreamRecordWindowSummary,
  StreamServiceStatus
} from '$lib/types';

const idleStatus: StreamServiceStatus = {
  running: false,
  host: '127.0.0.1',
  port: null,
  overlay_url: null,
  using_fallback_port: false,
  last_error: null,
  started_at: null,
  active_from: null,
  active_window_offset: 0
};

export async function getStreamServiceStatus(): Promise<StreamServiceStatus> {
  if (!hasTauriRuntime()) {
    return idleStatus;
  }

  return call('get_stream_service_status');
}

export async function startStreamService(
  gamePath?: string | null
): Promise<StreamServiceStatus> {
  if (!hasTauriRuntime()) {
    return idleStatus;
  }

  return call('start_stream_service', buildStreamCommandArgs(gamePath, {}));
}

export async function stopStreamService(): Promise<StreamServiceStatus> {
  if (!hasTauriRuntime()) {
    return idleStatus;
  }

  return call('stop_stream_service');
}

export async function setStreamOverlayWindowOffset(
  offset: number,
  gamePath?: string | null
): Promise<StreamServiceStatus> {
  if (!hasTauriRuntime()) {
    return idleStatus;
  }

  return call(
    'set_stream_overlay_window_offset',
    buildStreamCommandArgs(gamePath, {
      offset: Math.max(0, Math.trunc(offset))
    })
  );
}

export async function loadStreamRecordWindowSummary(
  baseUrl: string | null
): Promise<StreamRecordWindowSummary> {
  if (!baseUrl) {
    return {
      total: 0,
      existing_before_start: 0,
      captured_since_start: 0
    };
  }

  try {
    const response = await fetch(`${baseUrl}/api/records/summary`);
    if (!response.ok) {
      return {
        total: 0,
        existing_before_start: 0,
        captured_since_start: 0
      };
    }

    const payload = (await response.json()) as Partial<StreamRecordWindowSummary>;
    return {
      total:
        typeof payload.total === 'number' && Number.isFinite(payload.total)
          ? Math.max(0, Math.trunc(payload.total))
          : 0,
      existing_before_start:
        typeof payload.existing_before_start === 'number' &&
        Number.isFinite(payload.existing_before_start)
          ? Math.max(0, Math.trunc(payload.existing_before_start))
          : 0,
      captured_since_start:
        typeof payload.captured_since_start === 'number' &&
        Number.isFinite(payload.captured_since_start)
          ? Math.max(0, Math.trunc(payload.captured_since_start))
          : 0
    };
  } catch {
    return {
      total: 0,
      existing_before_start: 0,
      captured_since_start: 0
    };
  }
}

export async function loadStreamRecordAtOffset(
  baseUrl: string | null,
  offset: number
): Promise<StreamRecordSummary | null> {
  if (!baseUrl) {
    return null;
  }

  try {
    const endpoint = new URL(`${baseUrl}/api/records/latest`);
    if (offset > 0) {
      endpoint.searchParams.set('offset', String(Math.max(0, Math.trunc(offset))));
    }

    const response = await fetch(endpoint);
    if (!response.ok) {
      return null;
    }

    const payload = (await response.json()) as StreamRecordSummary | null;
    return payload && typeof payload.id === 'string' ? payload : null;
  } catch {
    return null;
  }
}

export async function loadStreamRecordList(
  gamePath?: string | null,
  limit?: number | null
): Promise<StreamRecordSummary[]> {
  if (typeof limit === 'number' && limit <= 0) {
    return [];
  }

  if (!hasTauriRuntime()) {
    return [];
  }

  const invokeArgs =
    typeof limit === 'number'
      ? buildStreamCommandArgs(gamePath, {
          limit: Math.max(1, Math.trunc(limit))
        })
      : buildStreamCommandArgs(gamePath, {});
  const payload = await call('list_stream_overlay_records', invokeArgs);
  return Array.isArray(payload)
    ? payload.filter((item) => item && typeof item.id === 'string')
    : [];
}

export async function revealStreamRecordImage(
  recordId: string,
  gamePath?: string | null
): Promise<void> {
  if (!hasTauriRuntime()) {
    return;
  }

  await call(
    'reveal_stream_record_image',
    buildStreamCommandArgs(gamePath, {
      recordId
    })
  );
}

export async function deleteStreamRecord(
  recordId: string,
  gamePath?: string | null
): Promise<void> {
  if (!hasTauriRuntime()) {
    return;
  }

  await call(
    'delete_stream_record',
    buildStreamCommandArgs(gamePath, {
      recordId
    })
  );
}

export async function loadStreamRecordStripPreview(
  recordId: string,
  gamePath?: string | null
): Promise<string | null> {
  if (!hasTauriRuntime()) {
    return null;
  }

  return call(
    'load_stream_record_strip_preview',
    buildStreamCommandArgs(gamePath, {
      recordId
    })
  );
}

export async function loadStreamRecordStripPreviews(
  recordIds: string[],
  gamePath?: string | null
): Promise<Record<string, string>> {
  if (!hasTauriRuntime() || recordIds.length === 0) {
    return {};
  }

  return call(
    'load_stream_record_strip_previews',
    buildStreamCommandArgs(gamePath, {
      recordIds
    })
  );
}

export async function getStreamOverlayCropSettings(): Promise<StreamOverlayCropSettingsPayload> {
  if (!hasTauriRuntime()) {
    return {
      crop: {
        left: 0.342,
        top: 0.313,
        width: 0.58,
        height: 0.22
      },
      code: '',
      display_mode: 'current'
    };
  }

  return call('get_stream_overlay_crop_settings');
}

export async function saveStreamOverlayCropSettings(
  crop: StreamOverlayCropSettings
): Promise<StreamOverlayCropSettingsPayload> {
  if (!hasTauriRuntime()) {
    return {
      crop,
      code: '',
      display_mode: 'current'
    };
  }

  return call('save_stream_overlay_crop_settings', { crop });
}

export async function importStreamOverlayCropCode(
  code: string
): Promise<StreamOverlayCropSettingsPayload> {
  if (!hasTauriRuntime()) {
    return {
      crop: {
        left: 0.342,
        top: 0.313,
        width: 0.58,
        height: 0.22
      },
      code,
      display_mode: 'current'
    };
  }

  return call('import_stream_overlay_crop_code', { code });
}

export async function saveStreamOverlayDisplayMode(
  displayMode: StreamOverlayDisplayMode
): Promise<StreamOverlayCropSettingsPayload> {
  if (!hasTauriRuntime()) {
    return {
      crop: {
        left: 0.342,
        top: 0.313,
        width: 0.58,
        height: 0.22
      },
      code: '',
      display_mode: displayMode
    };
  }

  return call('save_stream_overlay_display_mode', { displayMode });
}

export async function detectStreamDbPath(
  gamePath?: string | null
): Promise<StreamDbPathInfo> {
  if (!hasTauriRuntime()) {
    return { found: false, path: null };
  }

  return call('detect_stream_db_path', buildStreamCommandArgs(gamePath, {}));
}
