<script lang="ts">
  import { onDestroy, onMount } from 'svelte';
  import { openUrl } from '@tauri-apps/plugin-opener';
  import StreamServiceCard from '$lib/components/stream/StreamServiceCard.svelte';
  import { loadPersistedCustomGamePath } from '$lib/installer/storage';
  import { locale } from '$lib/locale';
  import { formatMessage, messages } from '$lib/i18n';
  import {
    detectStreamDbPath,
    getStreamOverlayCropSettings,
    loadStreamRecordAtOffset,
    loadStreamRecordWindowSummary,
    setStreamOverlayWindowOffset,
    saveStreamOverlayDisplayMode,
    getStreamServiceStatus,
    importStreamOverlayCropCode,
    startStreamService,
    stopStreamService
  } from '$lib/stream/api';
  import type { StreamDbPathInfo } from '$lib/types';
  import { createStreamPageState } from '$lib/stream/state';
  import type {
    StreamOverlayDisplayMode,
    StreamRecordSummary,
    StreamRecordWindowSummary,
    StreamServiceStatus
  } from '$lib/types';

  export let title = '';
  export let eyebrow = '';
  export let gamePath: string | null = null;

  let status: StreamServiceStatus = {
    running: false,
    host: '127.0.0.1',
    port: null,
    overlay_url: null,
    using_fallback_port: false,
    last_error: null,
    started_at: null,
    active_from: null,
    active_window_offset: 0
  };
  let busy = false;
  let savingDisplayMode = false;
  let importingCropCode = false;
  let cropCodeInput = '';
  let cropCodeMessage = '';
  let displayMode: StreamOverlayDisplayMode = 'current';
  let copyMessage = '';
  let copyMessageTone: 'success' | 'error' | null = null;
  let copyMessageTimer: number | null = null;
  let recordWindowSummary: StreamRecordWindowSummary = {
    total: 0,
    existing_before_start: 0,
    captured_since_start: 0
  };
  let dbPathInfo: StreamDbPathInfo = { found: false, path: null };
  let selectedOffset = 0;
  let selectedRecord: StreamRecordSummary | null = null;
  let persistedGamePath = '';

  $: t = (
    key: keyof typeof messages.en,
    params?: Record<string, string | number>
  ): string => formatMessage($locale, key, params);
  $: panelTitle = title || t('streamTitle');
  $: panelEyebrow = eyebrow || ($locale === 'zh' ? '直播模式' : 'Stream Mode');
  $: pageState = createStreamPageState(status);
  $: requestedGamePath = gamePath?.trim() || persistedGamePath || null;
  $: baseUrl = status.overlay_url?.replace(/\/overlay$/, '') ?? null;
  $: previewUrl = status.overlay_url;
  $: calibrationUrl = baseUrl ? `${baseUrl}/settings` : null;
  $: isZh = $locale === 'zh';
  $: canStepEarlier =
    status.running &&
    recordWindowSummary.existing_before_start > 0 &&
    selectedOffset < recordWindowSummary.existing_before_start;
  $: canStepLater = status.running && selectedOffset > 0;
  $: overviewStartLabel = formatOverviewStartTime(
    selectedOffset > 0
      ? selectedRecord?.captured_at ?? status.started_at
      : status.started_at
  );
  $: overviewHeroLabel = selectedOffset > 0
    ? (selectedRecord?.title || (isZh ? '未知英雄' : 'Unknown hero'))
    : status.running
      ? isZh
        ? '当前开播点'
        : 'Current live start'
      : isZh
        ? '尚未开始'
        : 'Not started';
  $: overviewDescription = status.running
    ? isZh
      ? `overview 会展示从这个起始时间之后的记录；当前窗口内共有 ${recordWindowSummary.captured_since_start} 条开播后记录。`
      : `Overview shows records captured after this start time. There are ${recordWindowSummary.captured_since_start} record(s) in the current stream window.`
    : isZh
      ? '启动服务后，overview 才会按当前起始时间展示记录。'
      : 'Start the service to show overview records from the current start time.';

  onMount(() => {
    locale.init();
    persistedGamePath = loadPersistedCustomGamePath();
    window.requestAnimationFrame(() => {
      void initializePage();
    });
  });

  onDestroy(() => {
    clearCopyMessage();
  });

  async function initializePage() {
    try {
      const [nextStatus, cropSettings, nextDbPathInfo] = await Promise.all([
        getStreamServiceStatus(),
        getStreamOverlayCropSettings(),
        detectStreamDbPath(requestedGamePath)
      ]);

      status = nextStatus;
      selectedOffset = status.active_window_offset;
      cropCodeInput = cropSettings.code;
      displayMode = cropSettings.display_mode;
      cropCodeMessage = '';
      await refreshOverviewState();
      dbPathInfo = nextDbPathInfo;
      console.info('[stream-mode-panel] initialize', {
        requestedGamePath,
        status,
        dbPathInfo
      });
    } catch (error) {
      console.error('[stream-mode-panel] initialize failed', {
        requestedGamePath,
        error
      });
    }
  }

  async function handleStart() {
    busy = true;
    try {
      status = await startStreamService(requestedGamePath);
      selectedOffset = status.active_window_offset;
      await refreshOverviewState();
      dbPathInfo = await detectStreamDbPath(requestedGamePath);
      console.info('[stream-mode-panel] stream service started', {
        requestedGamePath,
        status,
        dbPathInfo
      });
    } catch (error) {
      console.error('[stream-mode-panel] start failed', {
        requestedGamePath,
        error
      });
      status = await getStreamServiceStatus();
      await refreshOverviewState();
    } finally {
      busy = false;
    }
  }

  async function handleStop() {
    busy = true;
    try {
      status = await stopStreamService();
      selectedOffset = status.active_window_offset;
      await refreshOverviewState();
      dbPathInfo = await detectStreamDbPath(requestedGamePath);
      console.info('[stream-mode-panel] stream service stopped', {
        requestedGamePath,
        status,
        dbPathInfo
      });
    } catch (error) {
      console.error('[stream-mode-panel] stop failed', {
        requestedGamePath,
        error
      });
      status = await getStreamServiceStatus();
      await refreshOverviewState();
    } finally {
      busy = false;
    }
  }

  async function refreshOverviewState() {
    const currentBaseUrl = status.overlay_url?.replace(/\/overlay$/, '') ?? null;
    if (!status.running || !currentBaseUrl) {
      recordWindowSummary = {
        total: 0,
        existing_before_start: 0,
        captured_since_start: 0
      };
      selectedRecord = null;
      selectedOffset = 0;
      return;
    }

    recordWindowSummary = await loadStreamRecordWindowSummary(currentBaseUrl);
    const maxSelectableOffset = Math.max(
      0,
      recordWindowSummary.existing_before_start
    );
    selectedOffset = Math.max(0, Math.min(selectedOffset, maxSelectableOffset));
    if (selectedOffset === 0) {
      selectedRecord = null;
      return;
    }

    const recordOffset =
      recordWindowSummary.captured_since_start + selectedOffset - 1;
    selectedRecord = await loadStreamRecordAtOffset(currentBaseUrl, recordOffset);
  }

  async function stepOverviewOffset(direction: 1 | -1) {
    if (direction === 1 && !canStepEarlier) {
      return;
    }
    if (direction === -1 && !canStepLater) {
      return;
    }

    const nextOffset = Math.max(0, selectedOffset + direction);

    try {
      status = await setStreamOverlayWindowOffset(nextOffset, requestedGamePath);
      selectedOffset = status.active_window_offset;
      await refreshOverviewState();
    } catch (error) {
      console.error('[stream-mode-panel] failed to update live window', {
        requestedGamePath,
        nextOffset,
        error
      });
      status = await getStreamServiceStatus();
      selectedOffset = status.active_window_offset;
      await refreshOverviewState();
    }
  }

  function formatOverviewStartTime(value: string | null): string {
    if (!value) {
      return isZh ? '尚未开始' : 'Not started';
    }

    const parsed = new Date(value);
    if (Number.isNaN(parsed.getTime())) {
      return value;
    }

    return new Intl.DateTimeFormat(isZh ? 'zh-CN' : 'en-US', {
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      hour12: false
    }).format(parsed);
  }

  async function copyUrl() {
    if (!previewUrl) return;

    try {
      await navigator.clipboard.writeText(previewUrl);
      showCopyMessage(isZh ? 'OBS 地址已复制' : 'OBS URL copied');
    } catch (error) {
      console.error(error);
      showCopyMessage(isZh ? '复制失败，请重试' : 'Copy failed');
    }
  }

  async function openPreview() {
    if (!previewUrl) return;

    try {
      await openUrl(previewUrl);
    } catch (error) {
      console.error(error);
    }
  }

  async function openCalibration() {
    if (!calibrationUrl) return;

    try {
      await openUrl(calibrationUrl);
    } catch (error) {
      console.error(error);
    }
  }

  async function importCropCode() {
    importingCropCode = true;
    cropCodeMessage = '';

    try {
      const payload = await importStreamOverlayCropCode(cropCodeInput.trim());
      cropCodeInput = payload.code;
      cropCodeMessage = isZh
        ? '裁切代码已保存，overlay 会在下次刷新时使用它。'
        : 'Crop code saved. The overlay will use it on the next refresh.';
    } catch (error) {
      console.error(error);
      cropCodeMessage =
        error instanceof Error
          ? error.message
          : isZh
            ? '导入裁切代码失败。'
            : 'Failed to import crop code.';
    } finally {
      importingCropCode = false;
    }
  }

  async function updateDisplayMode(nextMode: StreamOverlayDisplayMode) {
    if (savingDisplayMode || displayMode === nextMode) {
      return;
    }

    savingDisplayMode = true;
    try {
      const payload = await saveStreamOverlayDisplayMode(nextMode);
      displayMode = payload.display_mode;
      if (typeof window !== 'undefined') {
        window.dispatchEvent(
          new CustomEvent('bpp-stream-display-mode-change', {
            detail: {
              displayMode: payload.display_mode
            }
          })
        );
      }
    } catch (error) {
      console.error(error);
    } finally {
      savingDisplayMode = false;
    }
  }

  function clearCopyMessage() {
    if (copyMessageTimer !== null) {
      window.clearTimeout(copyMessageTimer);
      copyMessageTimer = null;
    }
  }

  function showCopyMessage(message: string) {
    copyMessageTone = message === (isZh ? '复制失败，请重试' : 'Copy failed')
      ? 'error'
      : 'success';
    copyMessage = message;
    clearCopyMessage();
    copyMessageTimer = window.setTimeout(() => {
      copyMessage = '';
      copyMessageTone = null;
      copyMessageTimer = null;
    }, 1800);
  }
</script>

<section class="stream-panel">
  <div class="stream-copy">
    <p class="stream-eyebrow">{panelEyebrow}</p>
    <h2>{panelTitle}</h2>
  </div>

  <div class="stream-grid">
    <StreamServiceCard
      {status}
      {pageState}
      {busy}
      {savingDisplayMode}
      {displayMode}
      {importingCropCode}
      previewUrl={previewUrl}
      {cropCodeInput}
      {cropCodeMessage}
      {copyMessage}
      {copyMessageTone}
      {overviewStartLabel}
      {overviewHeroLabel}
      {canStepEarlier}
      {canStepLater}
      countAfter={recordWindowSummary.captured_since_start}
      countBefore={recordWindowSummary.existing_before_start}
      {dbPathInfo}
      onStart={handleStart}
      onStop={handleStop}
      onCopyUrl={copyUrl}
      onOpenPreview={openPreview}
      onOpenCalibration={openCalibration}
      onStepEarlier={() => stepOverviewOffset(1)}
      onStepLater={() => stepOverviewOffset(-1)}
      onDisplayModeChange={updateDisplayMode}
      onCropCodeInput={(value) => {
        cropCodeInput = value;
        cropCodeMessage = '';
      }}
      onImportCropCode={importCropCode}
    />
  </div>
</section>

<style>
  .stream-panel {
    display: grid;
    gap: 0.85rem;
  }

  .stream-copy {
    display: grid;
    gap: 0.22rem;
    padding: 0 0.1rem;
  }

  .stream-eyebrow {
    margin: 0;
    font-family: 'Cinzel', serif;
    font-size: 0.5rem;
    letter-spacing: 0.24em;
    text-transform: uppercase;
    color: rgba(var(--color-accent-rgb), 0.52);
  }

  .stream-copy h2 {
    margin: 0;
    font-family: 'Cinzel', serif;
    font-size: 0.82rem;
    letter-spacing: 0.08em;
    text-transform: uppercase;
    color: rgba(232, 220, 194, 0.92);
  }

  .stream-grid {
    display: grid;
    grid-template-columns: minmax(0, 1fr);
    gap: 1rem;
  }
</style>
