import { writable, get } from 'svelte/store';
import { BILIBILI_URL, STEAM_BAZAAR_URL } from '../../config/endpoints.ts';

import type {
  EnvironmentInfo,
  InstallerContextPayload,
  LaunchOptionsPatchResult,
  LegacyRecordDirectoryInfo,
  SteamRunningInfo
} from '../../types.ts';
import type { ActionBusy, StepState } from '../state.ts';
import type { InstallRuntimeRisk } from '../install-guards.ts';
import { detectInstallerEnvironment } from '../detect-flow.ts';
import { parseRepairError, type RepairError } from '../repair-errors.ts';
import type { TranslateText } from '../selectors/types.ts';

function debugInstallLog(message: string, payload: Record<string, unknown>) {
  if (!import.meta.env.DEV) return;
  console.debug(`[install-controller] ${message}`, payload);
}

export function createInstallController(input: {
  hasTauriRuntime: () => boolean;
  isDebugInstallPreview: boolean;
  createInstallDebugEnvironment: () => EnvironmentInfo;
  initializeInstallerContextApi: () => Promise<InstallerContextPayload>;
  detectEnvironmentApi: (requestedGamePath?: string) => Promise<EnvironmentInfo>;
  detectSteamRunningApi: () => Promise<SteamRunningInfo>;
  closeSteamApi: () => Promise<unknown>;
  installBepinex: (
    steamPath: string,
    gamePath: string,
    skipSteamShutdown: boolean
  ) => Promise<unknown>;
  patchLaunchOptions: (
    steamPath: string,
    gamePath: string,
    skipSteamShutdown: boolean
  ) => Promise<LaunchOptionsPatchResult>;
  uninstallBppApi: (steamPath: string, gamePath: string) => Promise<unknown>;
  repairBppApi: (gamePath: string) => Promise<unknown>;
  getLegacyRecordDirectoryInfoApi: (
    gamePath: string
  ) => Promise<LegacyRecordDirectoryInfo>;
  openUrl: (url: string) => Promise<void>;
  openDialog: (options: {
    directory: boolean;
    multiple: boolean;
  }) => Promise<string | string[] | null>;
  getInstallRuntimeRisks: (input: {
    hasTauriRuntime: boolean;
    steamLaunchOptionsSupported: boolean;
    steamRunning: boolean;
  }) => InstallRuntimeRisk[];
  shouldShowInstallRiskModal: (risks: InstallRuntimeRisk[]) => boolean;
  persistCustomGamePath: (path: string) => void;
  persistDetectedGamePath: (path: string) => void;
  localized: (zh: string, en: string) => string;
  t: TranslateText;
  formatByteLabel: (bytes: number) => string;
}) {
  const env = writable<EnvironmentInfo | null>(null);
  const dotnetState = writable<StepState>('idle');
  const bazaarFound = writable(false);
  const bazaarChecking = writable(false);
  const bazaarInvalid = writable(false);
  const customGamePath = writable('');
  const detectedGamePath = writable('');
  const actionBusy = writable<ActionBusy>('idle');
  const showInstallModal = writable(false);
  const showRepairModal = writable(false);
  const repairAcknowledged = writable(false);
  const repairModalBody = writable('');
  const repairError = writable<RepairError | null>(null);
  const showLaunchOptionsWarningModal = writable(false);
  const showSteamQuitModal = writable(false);
  const installAcknowledged = writable(false);
  const installConfirmationBusy = writable(false);
  const pendingSteamAction = writable<'install' | 'uninstall' | null>(null);
  const steamActionBusy = writable(false);
  const showStreamMode = writable(false);

  function initializeCustomGamePath(path: string) {
    customGamePath.set(path);
  }

  function initializeDetectedGamePath(path: string) {
    detectedGamePath.set(path);
  }

  function persistCurrentGamePath() {
    input.persistCustomGamePath(get(customGamePath));
  }

  function syncDetectedGamePathCache(
    selectedPath: string | null,
    resolvedGamePath: string | null | undefined
  ) {
    if (selectedPath) {
      return;
    }

    const normalizedPath = resolvedGamePath?.trim() ?? '';
    detectedGamePath.set(normalizedPath);
    input.persistDetectedGamePath(normalizedPath);
  }

  function applyInstallDebugState() {
    env.set(input.createInstallDebugEnvironment());
    dotnetState.set('found');
    bazaarFound.set(true);
    bazaarInvalid.set(false);
  }

  function requestInstall(canInstall: boolean) {
    if (!canInstall) return;
    installAcknowledged.set(false);
    showInstallModal.set(true);
  }

  function closeLaunchOptionsWarningModal() {
    showLaunchOptionsWarningModal.set(false);
  }

  function closeRepairModal() {
    if (get(actionBusy) === 'repair') return;
    repairAcknowledged.set(false);
    repairError.set(null);
    showRepairModal.set(false);
  }

  function closeSteamQuitModal() {
    if (get(steamActionBusy)) return;
    showSteamQuitModal.set(false);
    pendingSteamAction.set(null);
  }

  async function initializeStartupContext() {
    if (input.isDebugInstallPreview) return;
    if (!input.hasTauriRuntime()) return;

    try {
      const context = await input.initializeInstallerContextApi();
      debugInstallLog('initializeInstallerContext resolved', { context });
    } catch (error) {
      // Initialization is best-effort. detect_environment falls back to
      // lazy init inside Rust if this ever fails, so the UI still works.
      console.error(error);
    }
  }

  async function detectEnvironment(selectedPath: string | null) {
    if (get(actionBusy) !== 'idle') return;

    if (input.isDebugInstallPreview) {
      applyInstallDebugState();
      return;
    }

    actionBusy.set('detect');
    dotnetState.set('detecting');

    try {
      const result = await detectInstallerEnvironment({
        requestedGamePath: selectedPath,
        detectEnvironment: input.detectEnvironmentApi
      });
      debugInstallLog('startup detectEnvironment result', {
        selectedPath,
        result
      });
      syncDetectedGamePathCache(selectedPath, result.env?.game_path ?? null);
      env.set(result.env);
      dotnetState.set(result.dotnetState);
      bazaarFound.set(result.bazaarFound);
      bazaarInvalid.set(result.bazaarInvalid);
    } finally {
      actionBusy.set('idle');
    }
  }

  async function checkPath(effectiveGamePath: string) {
    if (!effectiveGamePath) return;

    const customPathAtStart = get(customGamePath).trim() || null;

    bazaarChecking.set(true);
    bazaarInvalid.set(false);
    try {
      const result = await detectInstallerEnvironment({
        requestedGamePath: effectiveGamePath,
        detectEnvironment: input.detectEnvironmentApi
      });
      debugInstallLog('manual checkPath result', {
        effectiveGamePath,
        result
      });
      syncDetectedGamePathCache(
        customPathAtStart,
        result.env?.game_path ?? null
      );
      env.set(result.env);
      dotnetState.set(result.dotnetState);
      bazaarFound.set(result.bazaarFound);
      bazaarInvalid.set(result.bazaarInvalid);
    } catch (error) {
      console.error(error);
      // Intentionally keep the cached detected path: a failed check (e.g.,
      // transient IO/network error) shouldn't wipe the last known-good value.
      bazaarFound.set(false);
      bazaarInvalid.set(true);
    } finally {
      bazaarChecking.set(false);
    }
  }

  function resetBazaar() {
    bazaarFound.set(false);
    bazaarInvalid.set(false);
    customGamePath.set('');
  }

  function clearBazaarInvalid() {
    bazaarInvalid.set(false);
  }

  async function pickGamePath() {
    const selected = await input.openDialog({ directory: true, multiple: false });
    if (typeof selected === 'string') {
      customGamePath.set(selected.trim());
    }
  }

  async function maybeConfirmSteamQuit(action: 'uninstall') {
    const currentEnv = get(env);
    if (!input.hasTauriRuntime() || !currentEnv?.steam_launch_options_supported) {
      return false;
    }

    try {
      const steamInfo = await input.detectSteamRunningApi();
      if (!steamInfo.running) {
        return false;
      }
    } catch (error) {
      console.error(error);
      return false;
    }

    pendingSteamAction.set(action);
    showSteamQuitModal.set(true);
    return true;
  }

  async function detectInstallRuntimeRisks() {
    const currentEnv = get(env);
    if (!input.hasTauriRuntime()) {
      return [];
    }

    let steamRunning = false;
    if (currentEnv?.steam_launch_options_supported) {
      try {
        const steamInfo = await input.detectSteamRunningApi();
        steamRunning = steamInfo.running;
      } catch (error) {
        console.error(error);
      }
    }

    return input.getInstallRuntimeRisks({
      hasTauriRuntime: true,
      steamLaunchOptionsSupported: Boolean(currentEnv?.steam_launch_options_supported),
      steamRunning
    });
  }

  async function refreshAfterAction(selectedPath: string | null) {
    actionBusy.set('idle');
    await detectEnvironment(selectedPath);
  }

  async function installBundled(inputArgs: {
    canInstall: boolean;
    effectiveGamePath: string;
    selectedPath: string | null;
    skipSteamShutdown?: boolean;
  }) {
    if (!inputArgs.canInstall) return;

    if (input.isDebugInstallPreview) {
      actionBusy.set('install');
      await new Promise((resolve) => window.setTimeout(resolve, 450));
      env.update((currentEnv) =>
        currentEnv
          ? {
              ...currentEnv,
              bpp_version: currentEnv.bundled_bpp_version ?? 'debug-preview'
            }
          : currentEnv
      );
      actionBusy.set('idle');
      return;
    }

    actionBusy.set('install');
    try {
      const currentEnv = get(env);
      const steamPath = currentEnv?.steam_path?.trim() ?? '';
      await input.installBepinex(
        steamPath,
        inputArgs.effectiveGamePath,
        Boolean(inputArgs.skipSteamShutdown)
      );
      if (currentEnv?.steam_launch_options_supported) {
        const patchResult = await input.patchLaunchOptions(
          steamPath,
          inputArgs.effectiveGamePath,
          Boolean(inputArgs.skipSteamShutdown)
        );
        if (!patchResult.verified) {
          showLaunchOptionsWarningModal.set(true);
        }
      }
      await refreshAfterAction(inputArgs.selectedPath);
    } catch (error) {
      console.error(error);
      actionBusy.set('idle');
    }
  }

  async function confirmInstall(inputArgs: {
    canInstall: boolean;
    effectiveGamePath: string;
    selectedPath: string | null;
  }) {
    if (!get(installAcknowledged) || get(installConfirmationBusy)) return;

    installConfirmationBusy.set(true);

    try {
      const runtimeRisks = await detectInstallRuntimeRisks();
      if (input.shouldShowInstallRiskModal(runtimeRisks)) {
        showInstallModal.set(false);
        pendingSteamAction.set('install');
        showSteamQuitModal.set(true);
        return;
      }

      showInstallModal.set(false);
      await installBundled({
        canInstall: inputArgs.canInstall,
        effectiveGamePath: inputArgs.effectiveGamePath,
        selectedPath: inputArgs.selectedPath,
        skipSteamShutdown: false
      });
    } finally {
      installConfirmationBusy.set(false);
    }
  }

  async function uninstallBpp(inputArgs: {
    effectiveGamePath: string;
    selectedPath: string | null;
    skipPrompt?: boolean;
  }) {
    if (!inputArgs.effectiveGamePath || get(actionBusy) !== 'idle') return;

    if (!inputArgs.skipPrompt && (await maybeConfirmSteamQuit('uninstall'))) {
      return;
    }

    actionBusy.set('uninstall');
    try {
      await input.uninstallBppApi(
        get(env)?.steam_path ?? '',
        inputArgs.effectiveGamePath
      );
      await refreshAfterAction(inputArgs.selectedPath);
    } catch (error) {
      console.error(error);
      actionBusy.set('idle');
    }
  }

  async function confirmSteamQuitAndContinue(inputArgs: {
    canInstall: boolean;
    effectiveGamePath: string;
    selectedPath: string | null;
  }) {
    const action = get(pendingSteamAction);
    if (!action || get(steamActionBusy)) return;

    steamActionBusy.set(true);

    try {
      await input.closeSteamApi();
      showSteamQuitModal.set(false);
      pendingSteamAction.set(null);

      if (action === 'install') {
        await installBundled({
          canInstall: inputArgs.canInstall,
          effectiveGamePath: inputArgs.effectiveGamePath,
          selectedPath: inputArgs.selectedPath,
          skipSteamShutdown: false
        });
        return;
      }

      if (action === 'uninstall') {
        await uninstallBpp({
          effectiveGamePath: inputArgs.effectiveGamePath,
          selectedPath: inputArgs.selectedPath,
          skipPrompt: true
        });
      }
    } catch (error) {
      console.error(error);
    } finally {
      steamActionBusy.set(false);
    }
  }

  async function handleSteamQuitModalCancel(inputArgs: {
    canInstall: boolean;
    effectiveGamePath: string;
    selectedPath: string | null;
  }) {
    if (get(steamActionBusy)) return;

    if (get(pendingSteamAction) === 'install') {
      showSteamQuitModal.set(false);
      pendingSteamAction.set(null);
      await installBundled({
        canInstall: inputArgs.canInstall,
        effectiveGamePath: inputArgs.effectiveGamePath,
        selectedPath: inputArgs.selectedPath,
        skipSteamShutdown: true
      });
      return;
    }

    closeSteamQuitModal();
  }

  async function requestRepair(inputArgs: { effectiveGamePath: string }) {
    if (!inputArgs.effectiveGamePath || get(actionBusy) !== 'idle') return;

    let sizeLabel = '0 B';
    try {
      const info = await input.getLegacyRecordDirectoryInfoApi(
        inputArgs.effectiveGamePath
      );
      sizeLabel = input.formatByteLabel(info.total_bytes);
    } catch (error) {
      console.error(error);
    }

    repairModalBody.set(input.t('resetHistoryBody', { size: sizeLabel }));
    repairAcknowledged.set(false);
    repairError.set(null);
    showRepairModal.set(true);
  }

  async function repairBpp(inputArgs: {
    effectiveGamePath: string;
    selectedPath: string | null;
  }) {
    if (!inputArgs.effectiveGamePath || get(actionBusy) !== 'idle') return;

    // Keep the modal open until we know the outcome — closing it before the
    // backend call hides the spinner and (more importantly) leaves nowhere to
    // surface failure information.
    repairError.set(null);
    actionBusy.set('repair');
    try {
      await input.repairBppApi(inputArgs.effectiveGamePath);
      showRepairModal.set(false);
      repairAcknowledged.set(false);
      await refreshAfterAction(inputArgs.selectedPath);
    } catch (error) {
      console.error(error);
      repairError.set(parseRepairError(error));
      actionBusy.set('idle');
    }
  }

  async function launchGame(canLaunchGame: boolean) {
    if (!canLaunchGame) return;

    try {
      await input.openUrl(STEAM_BAZAAR_URL);
    } catch (error) {
      console.error(error);
    }
  }

  async function openBilibili(event?: MouseEvent) {
    event?.preventDefault();

    if (!input.hasTauriRuntime()) {
      if (typeof window !== 'undefined') {
        window.open(BILIBILI_URL, '_blank', 'noopener,noreferrer');
      }
      return;
    }

    try {
      await input.openUrl(BILIBILI_URL);
    } catch (error) {
      console.error(error);
    }
  }

  function toggleStreamMode() {
    showStreamMode.update((value) => !value);
  }

  return {
    env,
    dotnetState,
    bazaarFound,
    bazaarChecking,
    bazaarInvalid,
    customGamePath,
    detectedGamePath,
    actionBusy,
    showInstallModal,
    showRepairModal,
    repairAcknowledged,
    repairModalBody,
    repairError,
    showLaunchOptionsWarningModal,
    showSteamQuitModal,
    installAcknowledged,
    installConfirmationBusy,
    pendingSteamAction,
    steamActionBusy,
    showStreamMode,
    initializeCustomGamePath,
    initializeDetectedGamePath,
    initializeStartupContext,
    persistCurrentGamePath,
    requestInstall,
    closeLaunchOptionsWarningModal,
    closeRepairModal,
    closeSteamQuitModal,
    detectEnvironment,
    checkPath,
    resetBazaar,
    clearBazaarInvalid,
    pickGamePath,
    confirmInstall,
    uninstallBpp,
    confirmSteamQuitAndContinue,
    handleSteamQuitModalCancel,
    requestRepair,
    repairBpp,
    launchGame,
    openBilibili,
    toggleStreamMode
  };
}
