import { listen } from '@tauri-apps/api/event';
import { getCurrentWindow } from '@tauri-apps/api/window';
import { hasTauriRuntime } from '../../api/runtime';

/**
 * Emitted natively after the main window hides to the tray
 * (`HIDDEN_EVENT` in `src-tauri/src/main_window.rs`). A WebView hidden that
 * way need not report `visibilitychange`.
 */
export const MAIN_WINDOW_HIDDEN_EVENT = 'main-window-hidden';

/**
 * Calls `onResume` whenever the window regains focus or becomes visible. In
 * Tauri it follows native focus, falling back to browser focus and visibility
 * events outside Tauri or when native registration fails.
 */
export function observeWindowResume(onResume: () => void): () => void {
  let active = true;
  let unlisten: (() => void) | undefined;
  const resume = () => {
    if (active) onResume();
  };
  const visibilityChanged = () => {
    if (document.visibilityState === 'visible') resume();
  };
  const observeDom = () => {
    window.addEventListener('focus', resume);
    document.addEventListener('visibilitychange', visibilityChanged);
  };
  if (hasTauriRuntime()) {
    void getCurrentWindow()
      .onFocusChanged(({ payload: focused }) => {
        if (focused) resume();
      })
      .then((stop) => {
        if (active) unlisten = stop;
        else stop();
      })
      .catch(() => {
        if (active) observeDom();
      });
  } else {
    observeDom();
  }
  return () => {
    active = false;
    window.removeEventListener('focus', resume);
    document.removeEventListener('visibilitychange', visibilityChanged);
    unlisten?.();
  };
}

/**
 * Calls `onHidden` when the window hides to the tray or the page reports
 * itself hidden. Pair it with `observeWindowResume` to resume.
 */
export function observeWindowHidden(onHidden: () => void): () => void {
  let active = true;
  let unlisten: (() => void) | undefined;
  const hidden = () => {
    if (active) onHidden();
  };
  const visibilityChanged = () => {
    if (document.visibilityState === 'hidden') hidden();
  };
  document.addEventListener('visibilitychange', visibilityChanged);
  if (hasTauriRuntime()) {
    void listen(MAIN_WINDOW_HIDDEN_EVENT, hidden)
      .then((stop) => {
        if (active) unlisten = stop;
        else stop();
      })
      // The page then pauses only on `visibilitychange`.
      .catch(() => undefined);
  }
  return () => {
    active = false;
    document.removeEventListener('visibilitychange', visibilityChanged);
    unlisten?.();
  };
}
