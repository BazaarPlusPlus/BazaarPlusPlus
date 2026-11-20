import { useEffect, useState } from 'react';
import { commandClient } from '../../api/commandClient';
import type { LegacyDataPresence } from '../../types/backend';

export type HistoryUnavailableGuidance = 'reinstall' | null;

export function historyUnavailableGuidance(
  presence: LegacyDataPresence | null
): HistoryUnavailableGuidance {
  if (!presence?.game_path) return null;
  const hasV5 = presence.roots.includes('BazaarPlusPlusV5');
  if (hasV5 && presence.installed_mod_data_root === 'reinstall_required') {
    return 'reinstall';
  }
  return null;
}

/** Accept any selected directory because History may have no current database there. */
export function useHistoryUnavailableGuidance(
  active: boolean
): HistoryUnavailableGuidance {
  const [guidance, setGuidance] = useState<HistoryUnavailableGuidance>(null);

  useEffect(() => {
    if (!active) {
      setGuidance(null);
      return;
    }
    let current = true;
    commandClient.getLegacyDataPresence(null).then(
      (presence) => {
        if (current) setGuidance(historyUnavailableGuidance(presence));
      },
      () => {
        if (current) setGuidance(null);
      }
    );
    return () => {
      current = false;
    };
  }, [active]);

  return guidance;
}
