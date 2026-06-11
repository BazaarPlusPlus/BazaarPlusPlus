import { useCallback, useEffect, useRef, useState } from 'react';

/**
 * Holds a transient success/status message that auto-clears after `timeoutMs`.
 * Setting `null` cancels any pending timer; a non-null value (re)starts it.
 * Use for ephemeral feedback like "Copied" or "Install complete" that should
 * not linger on screen indefinitely.
 */
export function useTransientMessage(timeoutMs = 3000) {
  const [message, setMessageState] = useState<string | null>(null);
  const timer = useRef<number | null>(null);

  const clearTimer = useCallback(() => {
    if (timer.current !== null) {
      window.clearTimeout(timer.current);
      timer.current = null;
    }
  }, []);

  const setMessage = useCallback(
    (next: string | null) => {
      clearTimer();
      setMessageState(next);
      if (next !== null) {
        timer.current = window.setTimeout(() => {
          setMessageState(null);
          timer.current = null;
        }, timeoutMs);
      }
    },
    [clearTimer, timeoutMs]
  );

  useEffect(() => clearTimer, [clearTimer]);

  return [message, setMessage] as const;
}
