import type { CommandAdapter } from './commandAdapter';
import { SemanticProblemError } from './problems';
import type { HistoryRunDetail } from '../types/backend';
import {
  defaultCropSettings,
  emptyHistoryRunList,
  emptyInstallState,
  emptyLegacyDataPresence,
  emptyLegacyDataState,
  emptyRunDataCleanupPreview,
  emptyRunDataCleanupResult,
  emptyScreenshotCleanupPreview,
  emptyScreenshotCleanupResult,
  fallbackBootstrap,
  idleStreamStatus
} from './previewDefaults';

/** The site's fixed real Run, in `vp dev` only: tests and builds keep an empty History. */
async function previewRun(): Promise<HistoryRunDetail | null> {
  if (import.meta.env.MODE !== 'development') return null;
  return (await import('./preview-run.json')).default as HistoryRunDetail;
}

// Browser Preview reads the published catalog; there is no export to run.
const previewCardCatalog = {
  revision: 'published',
  current: true,
  gameFound: true,
  progress: null,
  failed: false,
  exportedAtMs: null,
  files: 0,
  bytes: 0
};

export function createPreviewCommands(native: CommandAdapter): CommandAdapter {
  return {
    getAppBootstrap: async () => fallbackBootstrap,
    setAppLocale: async () => null,
    getInstallState: async () => emptyInstallState,
    chooseGameDirectory: async () => ({ game_path: null }),
    installMod: (...args) => native.installMod(...args),
    resetBppData: (...args) => native.resetBppData(...args),
    resetBepinex: (...args) => native.resetBepinex(...args),
    getLegacyDataState: async () => emptyLegacyDataState,
    getLegacyDataPresence: async () => emptyLegacyDataPresence,
    deleteLegacyRoot: (...args) => native.deleteLegacyRoot(...args),
    uninstallMod: (...args) => native.uninstallMod(...args),
    launchGame: async () => null,
    endGameProcess: async () => false,
    getStreamStatus: async () => idleStreamStatus,
    ensureStreamSession: async () => idleStreamStatus,
    // Browser Preview serves no images, so History shows its unavailable notice.
    prepareHistoryThumbnails: async () => {
      throw new SemanticProblemError({
        code: 'history_thumbnails_unavailable',
        params: { operation: 'prepare_history_thumbnails' },
        diagnostic: null
      });
    },
    restartStreamSession: async () => idleStreamStatus,
    setStreamWindow: async () => idleStreamStatus,
    getOverlaySettings: async () => defaultCropSettings,
    saveOverlayDisplayMode: async (displayMode) => ({
      ...defaultCropSettings,
      display_mode: displayMode
    }),
    applyOverlayCropCode: async (code) => ({ ...defaultCropSettings, code }),
    resetOverlayCrop: async () => defaultCropSettings,
    listHistoryRuns: async () => {
      const run = await previewRun();
      return run
        ? {
            ...emptyHistoryRunList,
            summary: { runs: 1, videos: 0, win_rate: null },
            runs: [{ ...run.run, thumbnail_url: null }],
            heroes: [run.run.hero]
          }
        : emptyHistoryRunList;
    },
    getCardCatalog: async () => previewCardCatalog,
    rebuildCardCatalog: async () => previewCardCatalog,
    getHistoryRunDetail: async (runId) => {
      const run = await previewRun();
      return run?.run.run_id === runId ? run : null;
    },
    revealRunScreenshot: async () => null,
    revealBattleVideo: async () => null,
    deleteBattleVideo: (...args) => native.deleteBattleVideo(...args),
    previewStorageCleanup: async (scope) =>
      scope === 'screenshots'
        ? { scope, preview: emptyScreenshotCleanupPreview, plan_id: 0 }
        : { scope, preview: emptyRunDataCleanupPreview, plan_id: 0 },
    executeStorageCleanup: async (scope) =>
      scope === 'screenshots'
        ? { scope, result: emptyScreenshotCleanupResult }
        : { scope, result: emptyRunDataCleanupResult }
  } satisfies CommandAdapter;
}
