<script lang="ts">
  import { open } from '@tauri-apps/plugin-dialog';
  import { openUrl } from '@tauri-apps/plugin-opener';
  import { onMount } from 'svelte';
  import type { Update } from '@tauri-apps/plugin-updater';
  import AppModal from '$lib/components/AppModal.svelte';
  import type { EnvironmentInfo } from '$lib/types';
  import { locale } from '$lib/locale';
  import { formatMessage, messages } from '$lib/i18n';
  import InstallerHeader from '$lib/components/installer/InstallerHeader.svelte';
  import InstallerInstallPreviewModal from '$lib/components/installer/InstallerInstallPreviewModal.svelte';
  import InstallerResetHistoryModal from '$lib/components/installer/InstallerResetHistoryModal.svelte';
  import InstallerStatusSteps from '$lib/components/installer/InstallerStatusSteps.svelte';
  import InstallerSupportBar from '$lib/components/installer/InstallerSupportBar.svelte';
  import {
    detectBazaarRunning as detectBazaarRunningApi,
    detectDotnetRuntime as detectDotnetRuntimeApi,
    detectEnvironment as detectEnvironmentApi,
    detectSteamRunning as detectSteamRunningApi,
    getLegacyRecordDirectoryInfo as getLegacyRecordDirectoryInfoApi,
    installBepinex,
    patchLaunchOptions,
    repairBpp as repairBppApi,
    uninstallBpp as uninstallBppApi,
    verifyGamePath as verifyGamePathApi
  } from '$lib/installer/api';
  import {
    loadPersistedCustomGamePath,
    persistCustomGamePath
  } from '$lib/installer/storage';
  import {
    hasTauriRuntime,
    resolveInstallDebugPreview
  } from '$lib/installer/runtime';
  import {
    checkForAppUpdate,
    createInitialUpdaterSnapshot,
    createProgressLabel,
    downloadAndInstallUpdate,
    formatUpdaterError,
    type UpdaterSnapshot
  } from '$lib/updater';
  import {
    createPageState,
    selectCustomGamePath,
    type ActionBusy,
    type StepState
  } from '$lib/installer/state';
  import type { InstallConfirmationStep } from '$lib/installer/install-guards';
  import {
    resolveInstallConfirmationStep as resolveInstallConfirmation,
    resolveInstallContinuationAction
  } from '$lib/installer/install-guards';
  import { detectInstallerEnvironment } from '$lib/installer/detect-flow';

  let env: EnvironmentInfo | null = null;
  let dotnetState: StepState = 'idle';
  let bazaarFound = false;
  let bazaarChecking = false;
  let bazaarInvalid = false;
  let customGamePath = loadPersistedCustomGamePath();
  let actionBusy: ActionBusy = 'idle';
  const STEAM_BAZAAR_URL = 'steam://rungameid/1617400';
  const BILIBILI_URL = 'https://space.bilibili.com/3546978457750467';
  let showInstallModal = false;
  let showRepairModal = false;
  let repairAcknowledged = false;
  let repairModalBody = '';
  let showLaunchOptionsWarningModal = false;
  let showGameQuitModal = false;
  let showSteamQuitModal = false;
  let installAcknowledged = false;
  let installConfirmationBusy = false;
  let pendingSteamAction: 'install' | 'uninstall' | null = null;
  let updaterSnapshot: UpdaterSnapshot = createInitialUpdaterSnapshot();
  let pendingUpdate: Update | null = null;
  let showUpdaterModal = false;
  let updaterModalTitle = '';
  let updaterModalBody = '';
  let showUpdaterReviewModal = false;
  let updaterReviewBusy = false;
  let updaterCheckRequestId = 0;

  $: t = (
    key: keyof typeof messages.en,
    params?: Record<string, string | number>
  ): string => formatMessage($locale, key, params);

  const isDebugInstallPreview = resolveInstallDebugPreview({
    isDev: import.meta.env.DEV,
    search: typeof window !== 'undefined' ? window.location.search : '',
    hasTauriRuntime: hasTauriRuntime()
  });

  function formatBytes(bytes: number): string {
    if (bytes < 1024) {
      return `${bytes} B`;
    }

    const units = ['KB', 'MB', 'GB'];
    let value = bytes / 1024;
    let unitIndex = 0;

    while (value >= 1024 && unitIndex < units.length - 1) {
      value /= 1024;
      unitIndex += 1;
    }

    return `${value.toFixed(value >= 10 ? 0 : 1)} ${units[unitIndex]}`;
  }

  function applyInstallDebugState() {
    env = {
      steam_path: 'C:\\Program Files (x86)\\Steam',
      steam_launch_options_supported: true,
      game_path: 'C:\\Games\\The Bazaar',
      dotnet_version: '9.0.0',
      dotnet_ok: true,
      bepinex_installed: false,
      bpp_version: null,
      bundled_bpp_version: 'debug-preview'
    };
    dotnetState = 'found';
    bazaarFound = true;
    bazaarInvalid = false;
  }

  function requestInstall() {
    if (!canInstall) return;
    installAcknowledged = false;
    showInstallModal = true;
  }

  async function confirmInstall() {
    if (!installAcknowledged || installConfirmationBusy) return;

    installConfirmationBusy = true;

    try {
      const confirmationStep = await detectInstallConfirmationStep();
      const continuationAction =
        resolveInstallContinuationAction(confirmationStep);

      if (continuationAction === 'show_game_quit_modal') {
        showInstallModal = false;
        showGameQuitModal = true;
        return;
      }

      if (continuationAction === 'show_steam_quit_modal') {
        showInstallModal = false;
        pendingSteamAction = 'install';
        showSteamQuitModal = true;
        return;
      }

      showInstallModal = false;
      await installBundled();
    } finally {
      installConfirmationBusy = false;
    }
  }

  function closeGameQuitModal() {
    showGameQuitModal = false;
  }

  function closeLaunchOptionsWarningModal() {
    showLaunchOptionsWarningModal = false;
  }

  async function requestRepair() {
    if (!pageState.effectiveGamePath || actionBusy !== 'idle') return;

    let sizeLabel = '0 B';
    try {
      const info = await getLegacyRecordDirectoryInfoApi(pageState.effectiveGamePath);
      sizeLabel = formatBytes(info.total_bytes);
    } catch (e) {
      console.error(e);
    }

    repairModalBody = t('resetHistoryBody', { size: sizeLabel });
    repairAcknowledged = false;
    showRepairModal = true;
  }

  function closeRepairModal() {
    if (actionBusy === 'repair') return;
    repairAcknowledged = false;
    showRepairModal = false;
  }

  function closeSteamQuitModal() {
    showSteamQuitModal = false;
    pendingSteamAction = null;
  }

  async function confirmSteamQuitAndContinue() {
    const action = pendingSteamAction;
    showSteamQuitModal = false;
    pendingSteamAction = null;

    if (action === 'install') {
      await installBundled();
      return;
    }

    if (action === 'uninstall') {
      await uninstallBpp(true);
    }
  }

  async function confirmGameQuitAndContinue() {
    const confirmationStep = await detectInstallConfirmationStep();
    const continuationAction =
      resolveInstallContinuationAction(confirmationStep);

    if (continuationAction === 'show_game_quit_modal') {
      return;
    }

    showGameQuitModal = false;

    if (continuationAction === 'show_steam_quit_modal') {
      pendingSteamAction = 'install';
      showSteamQuitModal = true;
      return;
    }

    await installBundled();
  }

  async function detectEnvironment() {
    if (actionBusy !== 'idle') return;

    if (isDebugInstallPreview) {
      applyInstallDebugState();
      return;
    }

    actionBusy = 'detect';
    dotnetState = 'detecting';

    try {
      const result = await detectInstallerEnvironment({
        requestedGamePath: selectedPath,
        detectEnvironment: detectEnvironmentApi,
        detectDotnetRuntime: detectDotnetRuntimeApi,
        verifyGamePath: verifyGamePathApi
      });
      env = result.env;
      dotnetState = result.dotnetState;
      bazaarFound = result.bazaarFound;
      bazaarInvalid = result.bazaarInvalid;
    } finally {
      actionBusy = 'idle';
    }
  }

  async function checkForUpdatesOnStartup() {
    const requestId = ++updaterCheckRequestId;

    updaterSnapshot = {
      ...updaterSnapshot,
      status: hasTauriRuntime() ? 'checking' : 'unsupported',
      errorMessage: null
    };

    const result = await checkForAppUpdate();
    if (requestId !== updaterCheckRequestId || updaterSnapshot.status !== 'checking') {
      return;
    }

    updaterSnapshot = result.snapshot;
    pendingUpdate = result.update;
  }

  async function pickGamePath() {
    const selected = await open({ directory: true, multiple: false });
    if (typeof selected === 'string') {
      customGamePath = selected.trim();
    }
  }

  async function checkPath() {
    const path = pageState.effectiveGamePath;
    if (!path) return;

    bazaarChecking = true;
    bazaarInvalid = false;
    try {
      bazaarFound = await verifyGamePathApi(path);
      if (!bazaarFound) {
        bazaarInvalid = true;
      }
    } catch {
      bazaarInvalid = true;
    } finally {
      bazaarChecking = false;
    }
  }

  function resetBazaar() {
    bazaarFound = false;
    bazaarInvalid = false;
    customGamePath = '';
  }

  async function refreshAfterAction() {
    actionBusy = 'idle';
    await detectEnvironment();
  }

  async function maybeConfirmSteamQuit(
    action: 'install' | 'uninstall'
  ): Promise<boolean> {
    if (!hasTauriRuntime() || !env?.steam_launch_options_supported) {
      return false;
    }

    try {
      const steamInfo = await detectSteamRunningApi();
      if (!steamInfo.running) {
        return false;
      }
    } catch (error) {
      console.error(error);
      return false;
    }

    pendingSteamAction = action;
    showSteamQuitModal = true;
    return true;
  }

  async function detectInstallConfirmationStep(): Promise<InstallConfirmationStep> {
    if (!hasTauriRuntime()) {
      return 'proceed';
    }

    let gameRunning = false;
    try {
      const gameInfo = await detectBazaarRunningApi();
      gameRunning = gameInfo.running;
    } catch (error) {
      console.error(error);
    }

    let steamRunning = false;
    if (env?.steam_launch_options_supported) {
      try {
        const steamInfo = await detectSteamRunningApi();
        steamRunning = steamInfo.running;
      } catch (error) {
        console.error(error);
      }
    }

    return resolveInstallConfirmation({
      hasTauriRuntime: true,
      gameRunning,
      steamLaunchOptionsSupported: Boolean(env?.steam_launch_options_supported),
      steamRunning
    });
  }

  async function installBundled() {
    if (!canInstall) return;

    if (isDebugInstallPreview) {
      actionBusy = 'install';
      await new Promise((resolve) => window.setTimeout(resolve, 450));
      env = env
        ? { ...env, bpp_version: env.bundled_bpp_version ?? 'debug-preview' }
        : env;
      actionBusy = 'idle';
      return;
    }

    actionBusy = 'install';
    try {
      const steamPath = env?.steam_path?.trim() ?? '';
      await installBepinex(steamPath, pageState.effectiveGamePath);
      if (env?.steam_launch_options_supported) {
        const patchResult = await patchLaunchOptions(
          steamPath,
          pageState.effectiveGamePath
        );
        if (!patchResult.verified) {
          showLaunchOptionsWarningModal = true;
        }
      }
      await refreshAfterAction();
    } catch (e) {
      console.error(e);
      actionBusy = 'idle';
    }
  }

  async function uninstallBpp(skipPrompt = false) {
    if (!pageState.effectiveGamePath || actionBusy !== 'idle') return;

    if (!skipPrompt && (await maybeConfirmSteamQuit('uninstall'))) {
      return;
    }

    actionBusy = 'uninstall';
    try {
      await uninstallBppApi(env?.steam_path ?? '', pageState.effectiveGamePath);
      await refreshAfterAction();
    } catch (e) {
      console.error(e);
      actionBusy = 'idle';
    }
  }

  async function launchGame() {
    if (!canLaunchGame) return;

    try {
      await openUrl(STEAM_BAZAAR_URL);
    } catch (e) {
      console.error(e);
    }
  }

  async function openBilibili(event?: MouseEvent) {
    event?.preventDefault();

    if (!hasTauriRuntime()) {
      if (typeof window !== 'undefined') {
        window.open(BILIBILI_URL, '_blank', 'noopener,noreferrer');
      }
      return;
    }

    try {
      await openUrl(BILIBILI_URL);
    } catch (e) {
      console.error(e);
    }
  }

  function openUpdaterModal(title: string, body: string) {
    updaterModalTitle = title;
    updaterModalBody = body;
    showUpdaterModal = true;
  }

  function closeUpdaterModal() {
    showUpdaterModal = false;
  }

  function openUpdaterReviewModal() {
    showUpdaterReviewModal = true;
  }

  function closeUpdaterReviewModal() {
    if (updaterReviewBusy) {
      return;
    }

    showUpdaterReviewModal = false;
  }

  async function startPendingUpdateDownload(update: Update) {
    updaterSnapshot = {
      ...updaterSnapshot,
      status: 'downloading',
      errorMessage: null,
      progress: {
        downloadedBytes: 0,
        totalBytes: null
      }
    };

    try {
      await downloadAndInstallUpdate(update, (progress) => {
        updaterSnapshot = {
          ...updaterSnapshot,
          status: 'downloading',
          progress
        };
      });

      updaterSnapshot = {
        ...updaterSnapshot,
        status: 'installed'
      };
      openUpdaterModal(
        t('updaterInstalledTitle'),
        t('updaterInstalledBody', {
          version: updaterSnapshot.availableVersion ?? update.version
        })
      );
    } catch (error) {
      const errorMessage = formatUpdaterError(error);
      updaterSnapshot = {
        ...updaterSnapshot,
        status: 'error',
        errorMessage: errorMessage,
        progress: {
          downloadedBytes: 0,
          totalBytes: null
        }
      };

      openUpdaterModal(t('updaterErrorTitle'), t('updaterErrorBody', { message: errorMessage }));
    }
  }

  async function repairBpp() {
    if (!pageState.effectiveGamePath || actionBusy !== 'idle') return;

    showRepairModal = false;
    actionBusy = 'repair';
    try {
      await repairBppApi(pageState.effectiveGamePath);
      await refreshAfterAction();
    } catch (e) {
      console.error(e);
      actionBusy = 'idle';
    }
  }

  async function confirmUpdaterReview() {
    if (!pendingUpdate || updaterReviewBusy) {
      return;
    }

    updaterReviewBusy = true;
    try {
      await startPendingUpdateDownload(pendingUpdate);
      showUpdaterReviewModal = false;
    } finally {
      updaterReviewBusy = false;
    }
  }

  async function handleUpdaterAction() {
    if (updaterSnapshot.status === 'checking' || updaterSnapshot.status === 'downloading') {
      return;
    }

    if (!hasTauriRuntime()) {
      openUpdaterModal(
        t('updaterErrorTitle'),
        t('updaterErrorBody', { message: t('updaterUnsupported') })
      );
      return;
    }

    if (updaterSnapshot.status === 'available' && pendingUpdate) {
      openUpdaterReviewModal();
      return;
    }

    if (updaterSnapshot.status === 'installed') {
      openUpdaterModal(
        t('updaterInstalledTitle'),
        t('updaterInstalledBody', {
          version: updaterSnapshot.availableVersion ?? updaterSnapshot.currentVersion ?? 'unknown'
        })
      );
      return;
    }

    if (updaterSnapshot.status === 'error') {
      if (pendingUpdate) {
        openUpdaterReviewModal();
        return;
      }

      openUpdaterModal(
        t('updaterErrorTitle'),
        t('updaterErrorBody', {
          message: updaterSnapshot.errorMessage ?? t('updaterUnsupported')
        })
      );
      return;
    }

    if (updaterSnapshot.status === 'up-to-date') {
      openUpdaterModal(t('updaterCurrentTitle'), t('updaterCurrentBody'));
      return;
    }

    if (updaterSnapshot.status === 'idle') {
      await checkForUpdatesOnStartup();
      return;
    }

    if (updaterSnapshot.status === 'unsupported') {
      openUpdaterModal(
        t('updaterErrorTitle'),
        t('updaterErrorBody', { message: t('updaterUnsupported') })
      );
      return;
    }

    openUpdaterModal(
      t('updaterReadyTitle'),
      t('updaterReadyBody', {
        version: updaterSnapshot.availableVersion ?? 'unknown'
      })
    );
  }

  function clearBazaarInvalid() {
    bazaarInvalid = false;
  }

  $: selectedPath = selectCustomGamePath(customGamePath);
  $: modInstalled = Boolean(env?.bpp_version);
  $: bundledBppVersion = env?.bundled_bpp_version ?? null;
  $: installedBppVersion = env?.bpp_version ?? null;
  $: pageState = createPageState({
    actionBusy,
    bazaarFound,
    selectedGamePath: selectedPath,
    detectedGamePath: env?.game_path ?? null,
    isDebugInstallPreview,
    bundledBppVersion,
    installedBppVersion
  });
  $: hasPath = pageState.hasPath;
  $: versionMismatch = pageState.versionMismatch;
  $: isBusy = pageState.isBusy;
  $: canInstall = pageState.canInstall;
  $: canLaunchGame = pageState.canLaunchGame;
  $: dotnetDownloadUrl =
    $locale === 'zh'
      ? 'https://dotnet.microsoft.com/zh-cn/download'
      : 'https://dotnet.microsoft.com/en-us/download';
  $: localeBadge = $locale === 'zh' ? '中' : 'EN';
  $: localeButtonLabel = $locale === 'zh' ? 'Switch to English' : '切换到中文';
  $: updaterProgressLabel = createProgressLabel(updaterSnapshot.progress);
  $: updaterButtonLabel =
    updaterSnapshot.status === 'checking'
      ? t('updaterChecking')
      : updaterSnapshot.status === 'available'
        ? t('updaterReady', {
            version: updaterSnapshot.availableVersion ?? '...'
          })
        : updaterSnapshot.status === 'downloading'
          ? t('updaterDownloading', {
              progress: updaterProgressLabel ?? '...'
            })
          : updaterSnapshot.status === 'installed'
            ? t('updaterInstallReady', {
                version: updaterSnapshot.availableVersion ?? '...'
              })
            : updaterSnapshot.status === 'error'
              ? pendingUpdate
                ? t('updaterRetry')
                : t('updaterErrorState')
            : updaterSnapshot.status === 'unsupported'
              ? t('updaterUnsupported')
              : t('updaterCurrent');
  $: updaterButtonTitle =
    updaterSnapshot.status === 'available'
      ? t('updaterReadyTitle')
      : updaterSnapshot.status === 'downloading'
        ? t('updaterInstalling')
        : updaterSnapshot.status === 'installed'
          ? t('updaterInstalledTitle')
          : updaterSnapshot.status === 'error'
            ? t('updaterErrorTitle')
            : updaterButtonLabel;
  $: updaterButtonDisabled =
    updaterSnapshot.status === 'checking' || updaterSnapshot.status === 'downloading';
  $: updaterButtonHighlighted =
    updaterSnapshot.status === 'available' || updaterSnapshot.status === 'installed';
  $: persistCustomGamePath(customGamePath);

  onMount(() => {
    locale.init();
    void detectEnvironment();
    void checkForUpdatesOnStartup();
  });
</script>

<svelte:head>
  <title>{t('pageTitle')}</title>
</svelte:head>

<main class="shell">
  <InstallerInstallPreviewModal
    open={showInstallModal}
    bind:installAcknowledged
    confirming={installConfirmationBusy}
    bilibiliUrl={BILIBILI_URL}
    onOpenBilibili={openBilibili}
    onConfirm={confirmInstall}
  />

  <InstallerResetHistoryModal
    open={showRepairModal}
    body={repairModalBody}
    bind:acknowledged={repairAcknowledged}
    confirming={actionBusy === 'repair'}
    onConfirm={repairBpp}
    onCancel={closeRepairModal}
  />

  <AppModal
    open={showLaunchOptionsWarningModal}
    eyebrow="BazaarPlusPlus"
    title={t('launchOptionsWarningTitle')}
    body={t('launchOptionsWarningBody')}
    confirmText={t('actionClose')}
    onConfirm={closeLaunchOptionsWarningModal}
  />

  <AppModal
    open={showGameQuitModal}
    eyebrow="BazaarPlusPlus"
    title={t('gameQuitTitle')}
    body={t('gameQuitBody')}
    confirmText={t('actionGameClosed')}
    cancelText={t('actionClose')}
    showCancel={true}
    onConfirm={confirmGameQuitAndContinue}
    onCancel={closeGameQuitModal}
  />

  <AppModal
    open={showSteamQuitModal}
    eyebrow="BazaarPlusPlus"
    title={t('steamQuitTitle')}
    body={t('steamQuitBody')}
    confirmText={t('actionQuitSteam')}
    cancelText={t('actionClose')}
    showCancel={true}
    onConfirm={confirmSteamQuitAndContinue}
    onCancel={closeSteamQuitModal}
  />

  <AppModal
    open={showUpdaterReviewModal}
    eyebrow="BazaarPlusPlus"
    title={t('updaterReviewTitle')}
    body={t('updaterReviewBody', {
      version: updaterSnapshot.availableVersion ?? pendingUpdate?.version ?? 'unknown'
    })}
    confirmText={t('updaterReviewConfirm')}
    cancelText={t('updaterReviewCancel')}
    showCancel={true}
    confirmBusy={updaterReviewBusy}
    confirmBusyText={t('updaterInstalling')}
    onConfirm={confirmUpdaterReview}
    onCancel={closeUpdaterReviewModal}
  />

  <AppModal
    open={showUpdaterModal}
    eyebrow="BazaarPlusPlus"
    title={updaterModalTitle}
    body={updaterModalBody}
    confirmText={t('actionClose')}
    onConfirm={closeUpdaterModal}
  />

  <InstallerHeader
    kicker={t('kicker')}
    subtitle={t('subtitle')}
    {localeBadge}
    {localeButtonLabel}
    bilibiliUrl={BILIBILI_URL}
    onOpenBilibili={openBilibili}
    {updaterButtonLabel}
    {updaterButtonTitle}
    {updaterButtonDisabled}
    {updaterButtonHighlighted}
    onOpenUpdater={handleUpdaterAction}
  />

  <InstallerStatusSteps
    {env}
    {dotnetState}
    {modInstalled}
    {versionMismatch}
    {bundledBppVersion}
    {installedBppVersion}
    {bazaarFound}
    {bazaarChecking}
    {bazaarInvalid}
    bind:customGamePath
    {hasPath}
    {isBusy}
    {actionBusy}
    {canInstall}
    {canLaunchGame}
    {dotnetDownloadUrl}
    effectiveGamePath={pageState.effectiveGamePath}
    {t}
    onPickGamePath={pickGamePath}
    onCheckPath={checkPath}
    onRequestInstall={requestInstall}
    onRepair={requestRepair}
    onUninstall={uninstallBpp}
    onLaunchGame={launchGame}
    onResetBazaar={resetBazaar}
    onCustomGamePathInput={clearBazaarInvalid}
  />

  <InstallerSupportBar />

  <footer class="footer" aria-hidden="true">
    <div class="rule">
      <span></span><span class="diamond small">+</span><span></span>
    </div>
    <p>{t('footer')}</p>
  </footer>
</main>

<style>
  .shell {
    width: 100%;
    max-width: 560px;
    margin: 0 auto;
    padding: 1.25rem 1rem 1.75rem;
    display: grid;
    gap: 0.85rem;
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
  .rule {
    width: 100%;
    display: flex;
    align-items: center;
    gap: 0.65rem;
    color: rgba(200, 148, 55, 0.35);
  }

  .rule span:first-child,
  .rule span:last-child {
    flex: 1;
    height: 1px;
    background: linear-gradient(
      90deg,
      transparent,
      rgba(200, 148, 55, 0.3) 40%,
      rgba(200, 148, 55, 0.3) 60%,
      transparent
    );
  }

  .diamond {
    font-size: 0.55rem;
    color: rgba(205, 150, 60, 0.55);
  }
  .diamond.small {
    font-size: 0.42rem;
  }

  .footer {
    text-align: center;
    display: grid;
    gap: 0.4rem;
  }

  .footer p {
    margin: 0;
    font-family: 'Cinzel', serif;
    font-size: 0.5rem;
    letter-spacing: 0.3em;
    text-transform: uppercase;
    color: rgba(140, 110, 60, 0.35);
  }

  @media (max-width: 520px) {
    .shell {
      padding: 1rem 0.85rem 1.5rem;
    }
  }
</style>
