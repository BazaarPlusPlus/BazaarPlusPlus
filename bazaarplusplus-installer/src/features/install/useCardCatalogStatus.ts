import { useCallback, useEffect, useState } from 'react';
import { commandClient } from '../../api/commandClient';
import type { CardCatalogStatus } from '../../types/backend';

const EXPORT_POLL_MS = 1000;

/**
 * The game assets the installer exports from the installed game. Reading the status starts an
 * export when the game has changed; while one runs the status is read again every second.
 */
export function useCardCatalogStatus() {
  const [status, setStatus] = useState<CardCatalogStatus | null>(null);
  const [rebuilds, setRebuilds] = useState(0);
  useEffect(() => {
    let active = true;
    let timer: ReturnType<typeof setTimeout> | undefined;
    const poll = async (rebuild: boolean) => {
      try {
        const next = await (rebuild
          ? commandClient.rebuildCardCatalog()
          : commandClient.getCardCatalog());
        if (!active) return;
        setStatus(next);
        if (next.progress)
          timer = setTimeout(() => void poll(false), EXPORT_POLL_MS);
      } catch {
        // Cards keep the names their Battle snapshots recorded.
      }
    };
    void poll(rebuilds > 0);
    return () => {
      active = false;
      clearTimeout(timer);
    };
  }, [rebuilds]);
  const rebuild = useCallback(() => setRebuilds((n) => n + 1), []);
  return { status, rebuild };
}
