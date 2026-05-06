const CUSTOM_GAME_PATH_STORAGE_KEY = 'bppinstaller:custom-game-path';
const DETECTED_GAME_PATH_STORAGE_KEY = 'bppinstaller:detected-game-path';

function getLocalStorage(): Storage | null {
  if (typeof window === 'undefined') return null;
  return window.localStorage;
}

export function loadPersistedCustomGamePath(): string {
  return getLocalStorage()?.getItem(CUSTOM_GAME_PATH_STORAGE_KEY)?.trim() ?? '';
}

export function persistCustomGamePath(path: string) {
  const localStorage = getLocalStorage();
  if (!localStorage) return;
  const normalizedPath = path.trim();
  if (normalizedPath) {
    localStorage.setItem(CUSTOM_GAME_PATH_STORAGE_KEY, normalizedPath);
    return;
  }
  localStorage.removeItem(CUSTOM_GAME_PATH_STORAGE_KEY);
}

export function loadPersistedDetectedGamePath(): string {
  return getLocalStorage()?.getItem(DETECTED_GAME_PATH_STORAGE_KEY)?.trim() ?? '';
}

export function persistDetectedGamePath(path: string) {
  const localStorage = getLocalStorage();
  if (!localStorage) return;
  const normalizedPath = path.trim();
  if (normalizedPath) {
    localStorage.setItem(DETECTED_GAME_PATH_STORAGE_KEY, normalizedPath);
    return;
  }
  localStorage.removeItem(DETECTED_GAME_PATH_STORAGE_KEY);
}
