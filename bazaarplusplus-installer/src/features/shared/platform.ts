// The WebView user agent is available both in Tauri and in Browser Preview.
export function isWindowsPlatform(): boolean {
  return (
    typeof navigator !== 'undefined' && navigator.userAgent.includes('Windows')
  );
}
