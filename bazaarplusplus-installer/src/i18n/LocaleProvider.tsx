import {
  createContext,
  use,
  useCallback,
  useEffect,
  useMemo,
  useState
} from 'react';
import type { ReactNode } from 'react';
import { invokeCommand } from '../api/tauri';
import { hasTauriRuntime } from '../api/runtime';
import {
  LOCALE_STORAGE_KEY,
  formatMessage,
  messages,
  resolveInitialLocale,
  type Locale,
  type MessageKey,
  type TranslateParams
} from './messages';

export type Translate = (key: MessageKey, params?: TranslateParams) => string;

export interface I18nController {
  locale: Locale;
  t: Translate;
  toggle: () => void;
  setLocale: (locale: Locale) => void;
}

const I18nContext = createContext<I18nController | null>(null);

export function LocaleProvider({ children }: { children: ReactNode }) {
  const [locale, setLocale] = useState<Locale>(resolveInitialLocale);

  // Locale is authoritative on the frontend: persist it to localStorage so it
  // survives a reload, reflect it on <html lang>, and sync the desktop tray.
  useEffect(() => {
    if (typeof document !== 'undefined') {
      document.documentElement.lang = messages[locale].htmlLang;
    }
    if (typeof window !== 'undefined') {
      window.localStorage.setItem(LOCALE_STORAGE_KEY, locale);
    }
    void syncTrayLocale(locale);
  }, [locale]);

  const toggle = useCallback(
    () => setLocale((current) => (current === 'zh' ? 'en' : 'zh')),
    []
  );

  const t = useCallback<Translate>(
    (key, params) => formatMessage(locale, key, params),
    [locale]
  );

  const value = useMemo<I18nController>(
    () => ({ locale, t, toggle, setLocale }),
    [locale, t, toggle]
  );

  return <I18nContext.Provider value={value}>{children}</I18nContext.Provider>;
}

export function useI18n(): I18nController {
  const controller = use(I18nContext);
  if (!controller) {
    throw new Error('useI18n must be used inside LocaleProvider.');
  }
  return controller;
}

async function syncTrayLocale(locale: Locale) {
  if (!hasTauriRuntime()) {
    return;
  }
  try {
    await invokeCommand('set_app_locale', { locale });
  } catch {
    // The web UI still switches even when the desktop tray sync fails.
  }
}
