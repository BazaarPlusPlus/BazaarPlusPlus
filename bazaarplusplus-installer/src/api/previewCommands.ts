import type { CommandAdapter } from './commandAdapter';
import {
  defaultCropSettings,
  emptyHistoryRunList,
  emptyInstallState,
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
    setAppLocale: async (locale) => ({ locale }),
    getInstallState: async () => emptyInstallState,
    chooseGameDirectory: async () => ({ game_path: null }),
    installMod: (...args) => native.installMod(...args),
    resetBppData: (...args) => native.resetBppData(...args),
    resetBepinex: (...args) => native.resetBepinex(...args),
    uninstallMod: (...args) => native.uninstallMod(...args),
    launchGame: async () => ({ ok: true }),
    endGameProcess: async () => false,
    getStreamStatus: async () => idleStreamStatus,
    ensureStreamSession: async () => idleStreamStatus,
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
    deleteRunVideos: async () => emptyHistoryRunList,
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
