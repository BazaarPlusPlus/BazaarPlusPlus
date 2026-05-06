<script lang="ts">
  import { open } from '@tauri-apps/plugin-dialog';
  import { openUrl } from '@tauri-apps/plugin-opener';
  import { onMount } from 'svelte';
  import AppModal from '$lib/components/AppModal.svelte';
  import { BILIBILI_URL } from '$lib/config/endpoints';
  import { locale } from '$lib/locale';
  import { formatMessage, messages } from '$lib/i18n';
  import InstallerHeader from '$lib/components/installer/InstallerHeader.svelte';
  import InstallerFooter from '$lib/components/installer/InstallerFooter.svelte';
  import InstallerPageContent from '$lib/components/installer/InstallerPageContent.svelte';
  import InstallerPageModals from '$lib/components/installer/InstallerPageModals.svelte';
  import InstallerSupportBar from '$lib/components/installer/InstallerSupportBar.svelte';
  import { createInstallController } from '$lib/installer/controllers/install-controller';
  import { createUpdaterController } from '$lib/installer/controllers/updater-controller';
  import {
    closeSteam as closeSteamApi,
    detectEnvironment as detectEnvironmentApi,
    detectSteamRunning as detectSteamRunningApi,
    getLegacyRecordDirectoryInfo as getLegacyRecordDirectoryInfoApi,
    initializeInstallerContext as initializeInstallerContextApi,
    installBepinex,
    patchLaunchOptions,
    repairBpp as repairBppApi,
    uninstallBpp as uninstallBppApi
  } from '$lib/installer/api';
  import {
    loadPersistedDetectedGamePath,
    loadPersistedCustomGamePath,
    persistDetectedGamePath,
    persistCustomGamePath
  } from '$lib/installer/storage';
  import {
    hasTauriRuntime,
    resolveInstallDebugPreview
  } from '$lib/installer/runtime';
  import { type PageState } from '$lib/installer/state';
  import {
    getInstallRuntimeRisks,
    shouldShowInstallRiskModal
  } from '$lib/installer/install-guards';
  import {
    createInstallDebugEnvironment,
    formatByteLabel,
    createInstallPageModel,
    type InstallPageModel,
    type InstallPageModelInput
  } from '$lib/installer/page-model';

  const isDebugInstallPreview = resolveInstallDebugPreview({
    isDev: import.meta.env.DEV,
    search: typeof window !== 'undefined' ? window.location.search : '',
    hasTauriRuntime: hasTauriRuntime()
  });

  function t(
    key: keyof typeof messages.en,
    params?: Record<string, string | number>
  ): string {
    return formatMessage($locale, key, params);
  }

  function localized(zh: string, en: string): string {
    return $locale === 'zh' ? zh : en;
  }

  const installController = createInstallController({
    hasTauriRuntime,
    isDebugInstallPreview,
    createInstallDebugEnvironment,
    detectEnvironmentApi,
    initializeInstallerContextApi,
    detectSteamRunningApi,
    closeSteamApi,
    installBepinex,
    patchLaunchOptions,
    uninstallBppApi,
    repairBppApi,
    getLegacyRecordDirectoryInfoApi,
    openUrl,
    openDialog: open,
    getInstallRuntimeRisks,
    shouldShowInstallRiskModal,
    persistCustomGamePath,
    persistDetectedGamePath,
    localized,
    t,
    formatByteLabel
  });
  installController.initializeCustomGamePath(loadPersistedCustomGamePath());
  installController.initializeDetectedGamePath(loadPersistedDetectedGamePath());

  const updaterController = createUpdaterController({
    hasTauriRuntime
  });

  const env = installController.env;
  const dotnetState = installController.dotnetState;
  const bazaarFound = installController.bazaarFound;
  const bazaarChecking = installController.bazaarChecking;
  const bazaarInvalid = installController.bazaarInvalid;
  const customGamePath = installController.customGamePath;
  const detectedGamePath = installController.detectedGamePath;
  const actionBusy = installController.actionBusy;
  const showInstallModal = installController.showInstallModal;
  const showRepairModal = installController.showRepairModal;
  const repairAcknowledged = installController.repairAcknowledged;
  const repairModalBody = installController.repairModalBody;
  const repairError = installController.repairError;
  const showLaunchOptionsWarningModal =
    installController.showLaunchOptionsWarningModal;
  const showSteamQuitModal = installController.showSteamQuitModal;
  const installAcknowledged = installController.installAcknowledged;
  const installConfirmationBusy = installController.installConfirmationBusy;
  const pendingSteamAction = installController.pendingSteamAction;
  const steamActionBusy = installController.steamActionBusy;
  const showStreamMode = installController.showStreamMode;

  const updaterSnapshot = updaterController.updaterSnapshot;
  const pendingUpdate = updaterController.pendingUpdate;
  const showUpdaterModal = updaterController.showUpdaterModal;
  const updaterModalTitle = updaterController.updaterModalTitle;
  const updaterModalBody = updaterController.updaterModalBody;
  const showUpdaterReviewModal = updaterController.showUpdaterReviewModal;
  const updaterReviewBusy = updaterController.updaterReviewBusy;

  let pageModelInput: InstallPageModelInput = {
    env: null,
    bazaarFound: false,
    customGamePath: '',
    cachedDetectedGamePath: $detectedGamePath,
    actionBusy: 'idle',
    showStreamMode: false,
    locale: 'zh',
    isDebugInstallPreview,
    updaterSnapshot: $updaterSnapshot,
    hasPendingUpdate: false,
    pendingSteamAction: null,
    localized,
    t
  };
  $: pageModelInput = {
    env: $env,
    bazaarFound: $bazaarFound,
    customGamePath: $customGamePath,
    cachedDetectedGamePath: $detectedGamePath,
    actionBusy: $actionBusy,
    showStreamMode: $showStreamMode,
    locale: $locale,
    isDebugInstallPreview,
    updaterSnapshot: $updaterSnapshot,
    hasPendingUpdate: Boolean($pendingUpdate),
    pendingSteamAction: $pendingSteamAction,
    localized,
    t
  };

  let pageModel: InstallPageModel = createInstallPageModel(pageModelInput);
  let pageState: PageState = pageModel.pageState;

  $: pageModel = createInstallPageModel(pageModelInput);
  $: pageState = pageModel.pageState;
  $: installController.persistCurrentGamePath();

  onMount(() => {
    locale.init();
    void (async () => {
      const updaterCheck = updaterController.checkForUpdatesOnStartup();
      await installController.initializeStartupContext();
      await Promise.allSettled([
        installController.detectEnvironment(pageModel.selectedPath),
        updaterCheck
      ]);
    })();
  });
</script>

<svelte:head>
  <title>{t('pageTitle')}</title>
</svelte:head>

<main class="shell">
  <InstallerPageModals
    bind:installAcknowledged={$installAcknowledged}
    bind:repairAcknowledged={$repairAcknowledged}
    showInstallModal={$showInstallModal}
    bilibiliUrl={BILIBILI_URL}
    installConfirmationBusy={$installConfirmationBusy}
    showRepairModal={$showRepairModal}
    repairModalBody={$repairModalBody}
    repairConfirming={$actionBusy === 'repair'}
    repairError={$repairError}
    showLaunchOptionsWarningModal={$showLaunchOptionsWarningModal}
    showSteamQuitModal={$showSteamQuitModal}
    steamModalTitle={pageModel.steamModalTitle}
    steamModalBody={pageModel.steamModalBody}
    steamModalCancelText={pageModel.steamModalCancelText}
    steamActionBusy={$steamActionBusy}
    showUpdaterReviewModal={$showUpdaterReviewModal}
    updaterReviewBody={t('updaterReviewBody', {
      version:
        $updaterSnapshot.availableVersion ??
        $pendingUpdate?.version ??
        'unknown'
    })}
    updaterReviewBusy={$updaterReviewBusy}
    showUpdaterModal={$showUpdaterModal}
    updaterModalTitle={$updaterModalTitle}
    updaterModalBody={$updaterModalBody}
    {t}
    onOpenBilibili={installController.openBilibili}
    onConfirmInstall={() =>
      installController.confirmInstall({
        canInstall: pageModel.canInstall,
        effectiveGamePath: pageState.effectiveGamePath,
        selectedPath: pageModel.selectedPath
      })}
    onConfirmRepair={() =>
      installController.repairBpp({
        effectiveGamePath: pageState.effectiveGamePath,
        selectedPath: pageModel.selectedPath
      })}
    onCancelRepair={installController.closeRepairModal}
    onCloseLaunchOptionsWarning={installController.closeLaunchOptionsWarningModal}
    onConfirmSteamQuit={() =>
      installController.confirmSteamQuitAndContinue({
        canInstall: pageModel.canInstall,
        effectiveGamePath: pageState.effectiveGamePath,
        selectedPath: pageModel.selectedPath
      })}
    onCancelSteamQuit={() =>
      installController.handleSteamQuitModalCancel({
        canInstall: pageModel.canInstall,
        effectiveGamePath: pageState.effectiveGamePath,
        selectedPath: pageModel.selectedPath
      })}
    onConfirmUpdaterReview={() => updaterController.confirmUpdaterReview(t)}
    onCancelUpdaterReview={updaterController.closeUpdaterReviewModal}
    onCloseUpdaterModal={updaterController.closeUpdaterModal}
  />

  <InstallerHeader
    kicker={t('kicker')}
    subtitle={pageModel.modeTitle}
    localeBadge={pageModel.localeBadge}
    localeButtonLabel={pageModel.localeButtonLabel}
    updaterButtonLabel={pageModel.updaterButtonLabel}
    updaterButtonTitle={pageModel.updaterButtonTitle}
    updaterButtonDisabled={pageModel.updaterButtonDisabled}
    updaterButtonHighlighted={pageModel.updaterButtonHighlighted}
    onOpenUpdater={() => updaterController.handleUpdaterAction(t)}
    streamModeActive={$showStreamMode}
    streamModeLabel={pageModel.modeToggleLabel}
    onToggleStreamMode={installController.toggleStreamMode}
  />

  <InstallerPageContent
    showStreamMode={$showStreamMode}
    locale={$locale}
    effectiveGamePath={pageState.effectiveGamePath}
    env={$env}
    dotnetState={$dotnetState}
    modInstalled={pageModel.modInstalled}
    versionMismatch={pageModel.versionMismatch}
    bundledBppVersion={pageModel.bundledBppVersion}
    installedBppVersion={pageModel.installedBppVersion}
    bazaarFound={$bazaarFound}
    bazaarChecking={$bazaarChecking}
    bazaarInvalid={$bazaarInvalid}
    bind:customGamePath={$customGamePath}
    hasPath={pageModel.hasPath}
    isBusy={pageModel.isBusy}
    actionBusy={$actionBusy}
    canInstall={pageModel.canInstall}
    canLaunchGame={pageModel.canLaunchGame}
    dotnetDownloadUrl={pageModel.dotnetDownloadUrl}
    {t}
    onPickGamePath={installController.pickGamePath}
    onCheckPath={() => installController.checkPath(pageState.effectiveGamePath)}
    onRequestInstall={() =>
      installController.requestInstall(pageModel.canInstall)}
    onRepair={() =>
      installController.requestRepair({
        effectiveGamePath: pageState.effectiveGamePath
      })}
    onUninstall={() =>
      installController.uninstallBpp({
        effectiveGamePath: pageState.effectiveGamePath,
        selectedPath: pageModel.selectedPath
      })}
    onLaunchGame={() => installController.launchGame(pageModel.canLaunchGame)}
    onResetBazaar={installController.resetBazaar}
    onCustomGamePathInput={installController.clearBazaarInvalid}
  />

  <InstallerSupportBar />
  <InstallerFooter text={t('footer')} />
</main>

<style>
  .shell {
    width: 100%;
    max-width: 900px;
    margin: 0 auto;
    padding: 1rem 1rem 1.45rem;
    display: grid;
    gap: 0.7rem;
    animation: fade-up 0.5s ease both;
  }
  @keyframes fade-up {
    from {
      opacity: 0;
      transform: translateY(14px);
    }
    to {
      opacity: 1;
      transform: translateY(0);
    }
  }
  @media (max-width: 520px) {
    .shell {
      padding: 0.9rem 0.82rem 1.35rem;
    }
  }
</style>
