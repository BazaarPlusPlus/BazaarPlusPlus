import { openUrl } from '@tauri-apps/plugin-opener';
import { commandClient } from '../../api/commandClient';
import type { CommandAdapter } from '../../api/commandAdapter';
import { hasTauriRuntime } from '../../api/runtime';
import type { StreamCommandPort, StreamOpener } from './streamWorkflow';

type StreamCommandAdapter = Pick<
  CommandAdapter,
  | 'ensureStreamSession'
  | 'getStreamStatus'
  | 'restartStreamSession'
  | 'setStreamWindow'
  | 'getOverlaySettings'
  | 'applyOverlayCropCode'
  | 'saveOverlayDisplayMode'
  | 'resetOverlayCrop'
>;

/** Semantic adapter shared by native and Browser Preview command clients. */
export function createStreamCommandPort(
  commands: StreamCommandAdapter
): StreamCommandPort {
  return {
    ensureSession: () => commands.ensureStreamSession(null),
    getStatus: () => commands.getStreamStatus(),
    restartSession: () => commands.restartStreamSession(null),
    setWindow: (offset) => commands.setStreamWindow(offset),
    loadCropSettings: () => commands.getOverlaySettings(),
    applyCropCode: (code) => commands.applyOverlayCropCode(code),
    saveDisplayMode: (displayMode) =>
      commands.saveOverlayDisplayMode(displayMode),
    resetCropSettings: () => commands.resetOverlayCrop()
  };
}

export const streamCommandPort = createStreamCommandPort(commandClient);

export const streamOpener: StreamOpener = {
  async open(url) {
    if (hasTauriRuntime()) {
      await openUrl(url);
      return;
    }

    window.open(url, '_blank', 'noopener,noreferrer');
  }
};
