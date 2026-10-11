import type { CommandAdapter } from './commandAdapter';
import { SemanticProblemError } from './problems';
import {
  defaultCropSettings,
  emptyHistoryRunList,
  emptyInstallState,
  emptyLegacyDataState,
  emptyRunDataCleanupPreview,
  emptyRunDataCleanupResult,
  emptyScreenshotCleanupPreview,
  emptyScreenshotCleanupResult,
  fallbackBootstrap,
  idleStreamStatus
} from './previewDefaults';

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
    listHistoryRuns: async () => emptyHistoryRunList,
    getHistoryRunDetail: async () => null,
    revealRunScreenshot: async () => null,
    revealBattleVideo: async () => null,
    deleteBattleVideo: (...args) => native.deleteBattleVideo(...args),
    previewStorageCleanup: async (scope) =>
      scope === 'screenshots'
        ? { scope, preview: emptyScreenshotCleanupPreview }
        : { scope, preview: emptyRunDataCleanupPreview },
    executeStorageCleanup: async (scope) =>
      scope === 'screenshots'
        ? { scope, result: emptyScreenshotCleanupResult }
        : { scope, result: emptyRunDataCleanupResult }
  } satisfies CommandAdapter;
}
