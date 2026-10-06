// Host-platform detection for copy that names OS-specific UI (Task Manager vs
// Activity Monitor, Start menu vs Applications) and for the shell chrome. The
// webview user agent is the only signal available in both the Tauri runtime and
// the browser preview.
export type HostPlatform = 'windows' | 'macos' | 'linux';

export function hostPlatform(): HostPlatform {
  const userAgent = typeof navigator !== 'undefined' ? navigator.userAgent : '';
  if (userAgent.includes('Windows')) {
    return 'windows';
  }
  if (userAgent.includes('Macintosh') || userAgent.includes('Mac OS X')) {
    return 'macos';
  }
  return 'linux';
}

export function isWindowsPlatform(): boolean {
  return hostPlatform() === 'windows';
}

export function isMacPlatform(): boolean {
  return hostPlatform() === 'macos';
}
