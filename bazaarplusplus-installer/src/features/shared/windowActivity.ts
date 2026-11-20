import { listen } from '@tauri-apps/api/event';
import { getCurrentWindow } from '@tauri-apps/api/window';
import { hasTauriRuntime } from '../../api/runtime';

/** A WebView hidden to the tray may omit `visibilitychange`, so Rust emits `HIDDEN_EVENT`. */
export const MAIN_WINDOW_HIDDEN_EVENT = 'main-window-hidden';

const noop: () => void = () => undefined;

/** Release registrations that finish after disposal so they cannot leak listeners. */
function nativeListener(
  register: () => Promise<() => void>,
  onFailure: () => void = noop
): () => void {
  let active = true;
  let stop: (() => void) | undefined;
  void register()
    .then((registered) => {
      if (active) stop = registered;
      else registered();
    })
    .catch(() => {
      if (active) onFailure();
    });
  return () => {
    active = false;
    stop?.();
  };
}

/** An uncovered macOS window can become visible without gaining focus. */
export function observeWindowResume(onResume: () => void): () => void {
  let active = true;
  const resume = () => {
    if (active) onResume();
  };
  const visibilityChanged = () => {
    if (document.visibilityState === 'visible') resume();
  };
  const observeDomFocus = () => window.addEventListener('focus', resume);
  document.addEventListener('visibilitychange', visibilityChanged);
  let stopNative = noop;
  if (hasTauriRuntime()) {
    stopNative = nativeListener(
      () =>
        getCurrentWindow().onFocusChanged(({ payload: focused }) => {
          if (focused) resume();
        }),
      observeDomFocus
    );
  } else {
    observeDomFocus();
  }
  return () => {
    active = false;
    window.removeEventListener('focus', resume);
    document.removeEventListener('visibilitychange', visibilityChanged);
    stopNative();
  };
}

export function observeWindowHidden(onHidden: () => void): () => void {
  let active = true;
  const hidden = () => {
    if (active) onHidden();
  };
  const visibilityChanged = () => {
    if (document.visibilityState === 'hidden') hidden();
  };
  document.addEventListener('visibilitychange', visibilityChanged);
  const stopNative = hasTauriRuntime()
    ? nativeListener(() => listen(MAIN_WINDOW_HIDDEN_EVENT, hidden))
    : noop;
  return () => {
    active = false;
    document.removeEventListener('visibilitychange', visibilityChanged);
    stopNative();
  };
}
