export function hasTauriRuntime(): boolean {
  return typeof window !== 'undefined' && '__TAURI_INTERNALS__' in window;
}

export function resolveInstallDebugPreview(options: {
  isDev: boolean;
  search: string;
  hasTauriRuntime: boolean;
}): boolean {
  return (
    options.isDev &&
    new URLSearchParams(options.search).get('debug-install') === '1' &&
    !options.hasTauriRuntime
  );
}
