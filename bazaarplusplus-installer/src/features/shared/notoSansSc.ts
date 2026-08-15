import { isWindowsPlatform } from './platform';

const NOTO_SANS_SC_CSS_URL =
  'https://fonts.googleapis.cn/css2?family=Noto+Sans+SC:wght@400;500;600;700&display=swap';
const CACHE_NAME = 'bpp-noto-sans-sc-css-v1';

let scheduled = false;

function installStyleSheet(css: string) {
  if (document.querySelector('style[data-bpp-noto-sans-sc]')) return;

  const style = document.createElement('style');
  style.dataset.bppNotoSansSc = 'true';
  style.textContent = css;
  document.head.append(style);
}

async function fetchStyleSheet() {
  const response = await fetch(NOTO_SANS_SC_CSS_URL, {
    credentials: 'omit',
    mode: 'cors'
  });
  if (!response.ok) {
    throw new Error(
      `Noto Sans SC stylesheet request failed: ${response.status}`
    );
  }
  return response;
}

async function loadPersistedStyleSheet() {
  if (typeof caches === 'undefined') {
    return (await fetchStyleSheet()).text();
  }

  try {
    const cache = await caches.open(CACHE_NAME);
    const cached = await cache.match(NOTO_SANS_SC_CSS_URL);
    if (cached) return cached.text();

    const response = await fetchStyleSheet();
    await cache.put(NOTO_SANS_SC_CSS_URL, response.clone());
    return response.text();
  } catch {
    // Cache Storage is unavailable in some WebView configurations. The font
    // mirror still supplies long-lived HTTP caching for its font files.
    return (await fetchStyleSheet()).text();
  }
}

async function loadNotoSansSc() {
  const root = document.documentElement;
  const windows = isWindowsPlatform();
  root.dataset.bppPlatform = windows ? 'windows' : 'macos';
  if (!windows) return;

  try {
    installStyleSheet(await loadPersistedStyleSheet());
    await document.fonts.load('400 1em "Noto Sans SC"', '因热爱而生');
    if (windows) root.dataset.bppNotoSansSc = 'ready';
  } catch {
    // The system font remains in use when the optional background download
    // is unavailable. A later app launch retries unless the stylesheet cache
    // was successfully written.
  }
}

/**
 * Starts the optional CJK font download after the first frame. It is kept out
 * of bundled assets and the stylesheet is retained in persistent Cache Storage.
 */
export function scheduleNotoSansScLoad() {
  if (scheduled || typeof window === 'undefined') return;
  scheduled = true;

  const start = () => void loadNotoSansSc();
  if (window.requestIdleCallback) {
    window.requestIdleCallback(start, { timeout: 2_000 });
  } else {
    window.setTimeout(start, 1_200);
  }
}
