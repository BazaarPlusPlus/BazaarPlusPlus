import { useCallback, useRef, useState, type ReactElement } from 'react';

import type { Locale } from '../../app/router';

/** Loaded only by the forms that need it, never proxied or cached (Cloudflare's rule). */
const TURNSTILE_SCRIPT_URL =
  'https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit';

// Cloudflare's always-pass test site key, for builds without a real key
// (local dev, `vp preview`, e2e). Deploys set VITE_TURNSTILE_SITE_KEY.
const TEST_SITE_KEY = '1x00000000000000000000AA';
const SITE_KEY: string =
  import.meta.env.VITE_TURNSTILE_SITE_KEY || TEST_SITE_KEY;

/** The form the token is for; the server checks it against the route. */
export type TurnstileAction = 'login' | 'register' | 'reset';

type RenderOptions = {
  sitekey: string;
  action: TurnstileAction;
  theme: 'dark';
  appearance: 'interaction-only';
  language: string;
  callback: (token: string) => void;
  'expired-callback': () => void;
  'error-callback': () => void;
};

type TurnstileApi = {
  render(container: HTMLElement, options: RenderOptions): string;
  reset(widgetId: string): void;
  remove(widgetId: string): void;
};

declare global {
  interface Window {
    turnstile?: TurnstileApi;
  }
}

let loading: Promise<TurnstileApi> | null = null;

function loadTurnstile(): Promise<TurnstileApi> {
  if (window.turnstile) return Promise.resolve(window.turnstile);
  loading ??= new Promise((resolve, reject) => {
    const script = document.createElement('script');
    script.src = TURNSTILE_SCRIPT_URL;
    script.async = true;
    script.addEventListener('load', () =>
      window.turnstile
        ? resolve(window.turnstile)
        : reject(new Error('Turnstile did not load'))
    );
    script.addEventListener('error', () => {
      // The next form mount tries again.
      loading = null;
      script.remove();
      reject(new Error('Turnstile did not load'));
    });
    document.head.append(script);
  });
  return loading;
}

export type Turnstile = {
  /** The current single-use token, or null until the check passes. */
  token: string | null;
  /** The script or the check failed; the form cannot be sent. */
  unavailable: boolean;
  /** Spends the token: call after every submit, success or failure. */
  reset: () => void;
  /** Where the widget renders; empty unless the visitor must interact. */
  widget: ReactElement;
};

/**
 * A managed Turnstile widget that shows itself only when it needs the visitor.
 * It lives while `widget` is mounted, so a form step can drop it.
 */
export function useTurnstile(
  locale: Locale,
  action: TurnstileAction
): Turnstile {
  const widgetId = useRef<string | null>(null);
  const [token, setToken] = useState<string | null>(null);
  const [unavailable, setUnavailable] = useState(false);

  const attach = useCallback(
    (element: HTMLDivElement) => {
      const mount = { live: true };
      const render = async () => {
        let api: TurnstileApi;
        try {
          api = await loadTurnstile();
        } catch {
          if (mount.live) setUnavailable(true);
          return;
        }
        if (!mount.live) return;
        widgetId.current = api.render(element, {
          sitekey: SITE_KEY,
          action,
          theme: 'dark',
          appearance: 'interaction-only',
          language: locale === 'zh' ? 'zh-CN' : 'en',
          callback: (next) => {
            setToken(next);
            setUnavailable(false);
          },
          // The widget refreshes an expired token by itself.
          'expired-callback': () => setToken(null),
          'error-callback': () => {
            setToken(null);
            setUnavailable(true);
          }
        });
      };
      void render();
      return () => {
        mount.live = false;
        if (widgetId.current !== null) {
          window.turnstile?.remove(widgetId.current);
        }
        widgetId.current = null;
        setToken(null);
        setUnavailable(false);
      };
    },
    [locale, action]
  );

  const reset = useCallback(() => {
    setToken(null);
    if (widgetId.current !== null) window.turnstile?.reset(widgetId.current);
  }, []);

  return {
    token,
    unavailable,
    reset,
    widget: <div ref={attach} className="empty:hidden" />
  };
}
