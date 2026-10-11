import { useEffect, useState } from 'react';
import { commandClient } from '../../api/commandClient';
import type { LegacyDataState } from '../../types/backend';

/**
 * Where History sends a user who has no current database: `import` when the
 * V5 import may run, `reinstall` when V5 history exists but the installed mod
 * still writes an old Data Root, otherwise `null` (the plain Install link).
 */
export type HistoryUnavailableGuidance = 'import' | 'reinstall' | null;

export function historyUnavailableGuidance(
  state: LegacyDataState | null
): HistoryUnavailableGuidance {
  if (!state?.game_path) return null;
  if (state.v5_import_eligible) return 'import';
  const hasV5 = state.roots.some((root) => root.name === 'BazaarPlusPlusV5');
  if (hasV5 && state.installed_mod_data_root === 'reinstall_required') {
    return 'reinstall';
  }
  return null;
}

/**
 * Reads the Legacy Roots of the selected game directory while History is
 * unavailable. The read accepts any directory, so it answers even where
 * History cannot resolve one; a failure keeps the plain Install link.
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
    commandClient.getLegacyDataState(null).then(
      (state) => {
        if (current) setGuidance(historyUnavailableGuidance(state));
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
