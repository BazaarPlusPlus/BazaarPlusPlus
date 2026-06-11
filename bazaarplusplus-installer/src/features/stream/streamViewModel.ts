import type { StreamServiceStatus } from '../../types/backend';

export type StreamOverlayState = 'error' | 'starting' | 'running' | 'idle';

export interface StreamViewModelInput {
  status: StreamServiceStatus;
  loading: boolean;
  action: string | null;
  error: string | null;
}

export interface StreamViewModel {
  state: StreamOverlayState;
  // Backend-provided error text (only set when state is 'error'); not localized.
  message: string | null;
  obsUrl: string | null;
  settingsUrl: string | null;
  canOpenOverlay: boolean;
  canOpenSettings: boolean;
  canRestart: boolean;
  isBusy: boolean;
}

export function createStreamViewModel(
  input: StreamViewModelInput
): StreamViewModel {
  const message = input.error ?? input.status.last_error;
  const isBusy = input.loading || input.action !== null;
  const running = input.status.running;
  const obsUrl = input.status.overlay_url;
  const settingsUrl = input.status.settings_url;

  if (message) {
    return {
      state: 'error',
      message,
      obsUrl,
      settingsUrl,
      canOpenOverlay: false,
      canOpenSettings: false,
      canRestart: !isBusy,
      isBusy
    };
  }

  if (input.loading) {
    return {
      state: 'starting',
      message: null,
      obsUrl,
      settingsUrl,
      canOpenOverlay: false,
      canOpenSettings: false,
      canRestart: false,
      isBusy
    };
  }

  return {
    state: running ? 'running' : 'idle',
    message: null,
    obsUrl,
    settingsUrl,
    canOpenOverlay: running && obsUrl !== null,
    canOpenSettings: running && settingsUrl !== null,
    canRestart: !isBusy,
    isBusy
  };
}
