import { useEffect, useState } from 'react';
import { commandClient } from '../../api/commandClient';
import type { LegacyDataPresence } from '../../types/backend';

/**
 * Where History sends a user who has no current database: `reinstall` when V5
 * history exists but the installed mod still writes an old Data Root, otherwise
 * `null` (the plain Install link).
 */
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

/**
 * Reads which Legacy Roots exist in the selected game directory while History
 * is unavailable, without measuring them (the Install page does that). The
 * read accepts any directory, so it answers even where History cannot resolve
 * one; a failure keeps the plain Install link.
 */
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
