import { openUrl } from '@tauri-apps/plugin-opener';
import { invokeCommand } from '../../api/tauri';
import { hasTauriRuntime } from '../../api/runtime';
import type {
  StreamOverlayCropSettingsPayload,
  StreamOverlayDisplayMode
} from '../../types/backend';
import { idleStreamStatus } from '../shared/streamSessionApi';
export {
  ensureStreamSession,
  getStreamStatus,
  idleStreamStatus
} from '../shared/streamSessionApi';

export const defaultCropSettings: StreamOverlayCropSettingsPayload = {
  crop: {
    left: 0.342,
    top: 0.313,
    width: 0.58,
    height: 0.22
  },
  code: '',
  display_mode: 'current'
};

export async function restartStreamSession() {
  if (!hasTauriRuntime()) {
    return idleStreamStatus;
  }

  return invokeCommand('restart_stream_session', {});
}

export async function setStreamWindowOffset(offset: number) {
  if (!hasTauriRuntime()) {
    return idleStreamStatus;
  }

  return invokeCommand('set_stream_window', {
    offset: Math.max(0, Math.trunc(offset))
  });
}

export async function loadCropSettings() {
  if (!hasTauriRuntime()) {
    return defaultCropSettings;
  }

  return invokeCommand('get_overlay_settings');
}

export async function applyCropCode(code: string) {
  if (!hasTauriRuntime()) {
    return { ...defaultCropSettings, code };
  }

  return invokeCommand('apply_overlay_crop_code', { code });
}

export async function saveDisplayMode(displayMode: StreamOverlayDisplayMode) {
  if (!hasTauriRuntime()) {
    return { ...defaultCropSettings, display_mode: displayMode };
  }

  return invokeCommand('save_overlay_display_mode', { displayMode });
}

export async function resetCropSettings() {
  if (!hasTauriRuntime()) {
    return defaultCropSettings;
  }

  return invokeCommand('reset_overlay_crop');
}

export async function openExternal(url: string) {
  if (hasTauriRuntime()) {
    await openUrl(url);
    return;
  }

  window.open(url, '_blank', 'noopener,noreferrer');
}
