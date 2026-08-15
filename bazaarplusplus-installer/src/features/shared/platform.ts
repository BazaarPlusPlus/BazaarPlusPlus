// Host-platform detection for copy that names OS-specific UI (Task Manager vs
// Activity Monitor, Start menu vs Applications). The webview user agent is the
// only signal available in both the Tauri runtime and the browser preview.
export function isWindowsPlatform(): boolean {
  return (
    typeof navigator !== 'undefined' && navigator.userAgent.includes('Windows')
  );
}
