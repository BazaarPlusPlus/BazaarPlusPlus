import { writable } from 'svelte/store';
import {
  resolveInitialLocale,
  messages,
  type Locale,
  defaultLocale
} from './i18n';
import { call } from './bridge/commands';

type SyncTrayLocale = (nextLocale: Locale) => Promise<unknown>;

export function createLocaleStore({
  syncTrayLocale = syncTrayLocaleToTauri
}: {
  syncTrayLocale?: SyncTrayLocale;
} = {}) {
  const { subscribe, set, update } = writable<Locale>(defaultLocale);

  function apply(nextLocale: Locale) {
    set(nextLocale);
    if (typeof document !== 'undefined') {
      document.documentElement.lang = messages[nextLocale].htmlLang;
    }
    if (typeof window !== 'undefined') {
      window.localStorage.setItem('locale', nextLocale);
    }
    void syncTrayLocale(nextLocale);
  }

  function toggle() {
    update((current) => {
      const next: Locale = current === 'zh' ? 'en' : 'zh';
      apply(next);
      return next;
    });
  }

  function init() {
    apply(resolveInitialLocale());
  }

  return { subscribe, apply, toggle, init };
}

export const locale = createLocaleStore();

async function syncTrayLocaleToTauri(nextLocale: Locale) {
  try {
    await call('set_tray_locale', { locale: nextLocale });
  } catch {
    // Locale still applies in the web UI when the Tauri backend is unavailable.
  }
}

export function handleLocaleToggle(event: MouseEvent) {
  event.preventDefault();
  event.stopPropagation();
  locale.toggle();
}
