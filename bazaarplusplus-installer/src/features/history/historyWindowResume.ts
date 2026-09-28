import { getCurrentWindow } from '@tauri-apps/api/window';
import { hasTauriRuntime } from '../../api/runtime';

export function observeHistoryWindowResume(onResume: () => void): () => void {
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
