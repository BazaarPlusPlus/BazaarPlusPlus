import { useEffect, useMemo, useRef, useSyncExternalStore } from 'react';
import { openUrl } from '@tauri-apps/plugin-opener';
import { commandClient } from '../../api/commandClient';
import { hasTauriRuntime } from '../../api/runtime';
import { useI18n } from '../../i18n/LocaleProvider';
import {
  createStreamWorkflow,
  type StreamClipboard,
  type StreamOpener,
  type StreamScheduler
} from './streamWorkflow';

const browserScheduler: StreamScheduler = {
  setInterval: (callback, delayMs) => window.setInterval(callback, delayMs),
  clearInterval: (handle) => window.clearInterval(handle as number),
  setTimeout: (callback, delayMs) => window.setTimeout(callback, delayMs),
  clearTimeout: (handle) => window.clearTimeout(handle as number)
};

const browserClipboard: StreamClipboard = {
  writeText: (value) => navigator.clipboard.writeText(value)
};

const streamOpener: StreamOpener = {
  async open(url) {
    if (hasTauriRuntime()) {
      await openUrl(url);
      return;
    }

    window.open(url, '_blank', 'noopener,noreferrer');
  }
};

export function useStreamPage() {
  // The workflow is created once, so the locale enters as a live read rather
  // than a captured value.
  const { locale } = useI18n();
  const localeRef = useRef(locale);
  localeRef.current = locale;

  const workflow = useMemo(
    () =>
      createStreamWorkflow({
        commands: commandClient,
        scheduler: browserScheduler,
        clipboard: browserClipboard,
        opener: streamOpener,
        currentLocale: () => localeRef.current
      }),
    []
  );
  const snapshot = useSyncExternalStore(
    workflow.subscribe,
    workflow.getSnapshot,
    workflow.getSnapshot
  );

  useEffect(() => {
    void workflow.start();
    return () => workflow.dispose();
  }, [workflow]);

  return { snapshot, intents: workflow.intents };
}
