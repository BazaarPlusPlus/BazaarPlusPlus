import { listen } from '@tauri-apps/api/event';
import { getCurrentWindow } from '@tauri-apps/api/window';
import { hasTauriRuntime } from '../../api/runtime';

/**
 * Emitted natively after the main window hides to the tray
 * (`HIDDEN_EVENT` in `src-tauri/src/main_window.rs`). A WebView hidden that
 * way need not report `visibilitychange`.
 */
export const MAIN_WINDOW_HIDDEN_EVENT = 'main-window-hidden';

const noop: () => void = () => undefined;

/**
 * Registers a native listener and returns its release. A registration that
 * lands after release is stopped at once; a failed one calls `onFailure`
 * unless already released.
 */
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

/**
 * Calls `onResume` whenever the window regains focus or the page becomes
 * visible. Visibility counts in every mode, because a page can become visible
 * without the window gaining focus (an uncovered macOS window). In Tauri focus
 * follows the native window, falling back to browser focus outside Tauri or
 * when native registration fails.
 */
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

/**
 * Calls `onHidden` when the window hides to the tray or the page reports
 * itself hidden. Pair it with `observeWindowResume` to resume.
 */
export function observeWindowHidden(onHidden: () => void): () => void {
  let active = true;
  const hidden = () => {
    if (active) onHidden();
  };
  const visibilityChanged = () => {
    if (document.visibilityState === 'hidden') hidden();
  };
  document.addEventListener('visibilitychange', visibilityChanged);
  // Without the native event the page pauses only on `visibilitychange`.
  const stopNative = hasTauriRuntime()
    ? nativeListener(() => listen(MAIN_WINDOW_HIDDEN_EVENT, hidden))
    : noop;
  return () => {
    active = false;
    document.removeEventListener('visibilitychange', visibilityChanged);
    stopNative();
  };
}
