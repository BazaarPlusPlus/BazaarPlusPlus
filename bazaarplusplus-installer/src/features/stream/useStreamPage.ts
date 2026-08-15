import { useEffect, useMemo, useRef, useSyncExternalStore } from 'react';
import { useI18n } from '../../i18n/LocaleProvider';
import { streamCommandPort, streamOpener } from './streamApi';
import {
  createStreamWorkflow,
  type StreamClipboard,
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

export function useStreamPage() {
  // The workflow is created once, so the locale enters as a live read rather
  // than a captured value.
  const { locale } = useI18n();
  const localeRef = useRef(locale);
  localeRef.current = locale;

  const workflow = useMemo(
    () =>
      createStreamWorkflow({
        commands: streamCommandPort,
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
