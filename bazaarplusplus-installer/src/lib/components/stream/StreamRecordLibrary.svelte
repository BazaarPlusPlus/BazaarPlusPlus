<script lang="ts">
  import { onDestroy, onMount } from 'svelte';
  import { loadPersistedCustomGamePath } from '$lib/installer/storage';
  import { locale } from '$lib/locale';
  import {
    deleteStreamRecord,
    getStreamOverlayCropSettings,
    getStreamServiceStatus,
    loadStreamRecordList,
    revealStreamRecordImage
  } from '$lib/stream/api';
  import { resolveHeroKey } from '$lib/stream/heroes';
  import type { StreamOverlayCropSettings, StreamOverlayDisplayMode, StreamRecordSummary } from '$lib/types';

  const badgeAssets = import.meta.glob(
    '../../../../src-tauri/resources/stream/badges/**/*.svg',
    {
      eager: true,
      import: 'default'
    }
  ) as Record<string, string>;

  const DEFAULT_CROP: StreamOverlayCropSettings = {
    left: 0.342,
    top: 0.313,
    width: 0.58,
    height: 0.22
  };

  export let gamePath: string | null = null;

  let records: StreamRecordSummary[] = [];
  let loading = false;
  let isZh = false;
  let persistedGamePath = '';
  let displayMode: StreamOverlayDisplayMode = 'current';
  let settingsPollTimer: number | null = null;
  let previewBaseUrl: string | null = null;
  let lastCropSignature = cropSignature(DEFAULT_CROP);
  let deletingRecordIds = new Set<string>();

  $: isZh = $locale === 'zh';
  $: requestedGamePath = gamePath?.trim() || persistedGamePath || null;

  onMount(() => {
    persistedGamePath = loadPersistedCustomGamePath();
    window.requestAnimationFrame(() => {
      void refreshOverlaySettings();
      void refreshPreviewBaseUrl();
      window.setTimeout(() => {
        void refreshRecords();
      }, 0);
    });

    const handleDisplayModeChange = (event: Event) => {
      const nextMode = (event as CustomEvent<{ displayMode?: string }>).detail?.displayMode;
      displayMode = normalizeDisplayMode(nextMode);
    };

    window.addEventListener(
      'bpp-stream-display-mode-change',
      handleDisplayModeChange as EventListener
    );

    settingsPollTimer = window.setInterval(() => {
      void refreshOverlaySettings();
      void refreshPreviewBaseUrl();
    }, 2000);

    return () => {
      window.removeEventListener(
        'bpp-stream-display-mode-change',
        handleDisplayModeChange as EventListener
      );
    };
  });

  onDestroy(() => {
    if (settingsPollTimer !== null) {
      window.clearInterval(settingsPollTimer);
      settingsPollTimer = null;
    }
  });

  async function refreshOverlaySettings() {
    try {
      const payload = await getStreamOverlayCropSettings();
      displayMode = normalizeDisplayMode(payload.display_mode);
      const nextCrop = payload.crop || { ...DEFAULT_CROP };
      const nextSignature = cropSignature(nextCrop);

      lastCropSignature = nextSignature;
    } catch (error) {
      console.error('[stream-record-library] refresh overlay settings failed', {
        error
      });
    }
  }

  async function refreshPreviewBaseUrl() {
    try {
      const status = await getStreamServiceStatus();
      previewBaseUrl = status.overlay_url?.replace(/\/overlay$/, '') ?? null;
    } catch (error) {
      console.error('[stream-record-library] refresh preview base url failed', {
        error
      });
    }
  }

  async function refreshRecords() {
    loading = true;
    console.info('[stream-record-library] refresh start', {
      requestedGamePath
    });
    try {
      await refreshOverlaySettings();
      records = await loadStreamRecordList(requestedGamePath);
      console.info('[stream-record-library] refresh success', {
        requestedGamePath,
        recordCount: records.length,
        recordIds: records.map((record) => record.id)
      });
    } catch (error) {
      console.error('[stream-record-library] refresh failed', {
        requestedGamePath,
        error
      });
      records = [];
    } finally {
      loading = false;
    }
  }

  async function revealRecordImage(recordId: string) {
    try {
      await revealStreamRecordImage(recordId, requestedGamePath);
      console.info('[stream-record-library] reveal image', {
        requestedGamePath,
        recordId
      });
    } catch (error) {
      console.error('[stream-record-library] reveal image failed', {
        requestedGamePath,
        recordId,
        error
      });
    }
  }

  async function deleteRecord(recordId: string) {
    if (deletingRecordIds.has(recordId)) {
      return;
    }

    deletingRecordIds = new Set([...deletingRecordIds, recordId]);

    try {
      await deleteStreamRecord(recordId, requestedGamePath);
      records = records.filter((record) => record.id !== recordId);
    } catch (error) {
      console.error('[stream-record-library] delete record failed', {
        requestedGamePath,
        recordId,
        error
      });
    } finally {
      const nextDeletingIds = new Set(deletingRecordIds);
      nextDeletingIds.delete(recordId);
      deletingRecordIds = nextDeletingIds;
    }
  }

  function normalizeDisplayMode(value: string | undefined): StreamOverlayDisplayMode {
    if (value === 'hero' || value === 'herohalf') {
      return value;
    }

    return 'current';
  }

  function resolveBadgeAsset(relativePath: string): string {
    return (
      badgeAssets[
        `../../../../src-tauri/resources/stream/badges/${relativePath}`
      ] || ''
    );
  }

  function getWinsBadgeAsset(wins: number | null, battles: number | null): string {
    if (wins === null || wins < 0) {
      return resolveBadgeAsset('wins/wins-0-mis.svg');
    }

    const safeWins = Math.max(0, Math.min(10, Math.trunc(wins)));
    if (safeWins === 10) {
      if (battles !== null && Math.trunc(battles) === 10) {
        return resolveBadgeAsset('wins/wins-10-dia.svg');
      }
      return resolveBadgeAsset('wins/wins-10-gld.svg');
    }
    if (safeWins >= 7) {
      return resolveBadgeAsset(`wins/wins-${safeWins}-slv.svg`);
    }
    if (safeWins >= 4) {
      return resolveBadgeAsset(`wins/wins-${safeWins}-brz.svg`);
    }
    return resolveBadgeAsset(`wins/wins-${safeWins}-mis.svg`);
  }

  function getInfoBadgeAsset(heroKey: string, battles: number | null): string {
    const safeBattles =
      typeof battles === 'number' && Number.isFinite(battles)
        ? Math.max(0, Math.min(20, Math.trunc(battles)))
        : 0;
    return resolveBadgeAsset(`info/info-${heroKey}-${safeBattles}.svg`);
  }

  function getHeroModeAsset(heroKey: string, mode: StreamOverlayDisplayMode): string {
    if (mode === 'hero') {
      return resolveBadgeAsset(`heroes/hero-${heroKey}.svg`);
    }
    return resolveBadgeAsset(`herohalf/herohalf-${heroKey}.svg`);
  }

  function resolveSecondaryBadgeSrc(record: StreamRecordSummary): string {
    const heroKey = resolveHeroKey(record.title || '');
    const battles =
      typeof record.battle_count === 'number' && Number.isFinite(record.battle_count)
        ? record.battle_count
        : null;

    if (displayMode === 'current') {
      return getInfoBadgeAsset(heroKey, battles);
    }

    return getHeroModeAsset(heroKey, displayMode);
  }

  function resolveRecordImageSrc(record: StreamRecordSummary): string | null {
    if (!previewBaseUrl || !record.image_url?.trim()) {
      return null;
    }

    return `${previewBaseUrl}${record.image_url}/strip?v=${lastCropSignature}`;
  }

  function cropSignature(value: StreamOverlayCropSettings): string {
    return [
      value.left,
      value.top,
      value.width,
      value.height
    ]
      .map((item) => Math.round(item * 10_000))
      .join(':');
  }

  function syncRecordPreviewHeight(node: HTMLElement) {
    const card = node.closest('.record-card');
    if (!card) {
      return {
        destroy() {}
      };
    }

    const cardElement = card as HTMLElement;

    const applyHeight = () => {
      const nextHeight = node.getBoundingClientRect().height;
      if (nextHeight > 0) {
        cardElement.style.setProperty('--record-preview-height', `${nextHeight}px`);
      }
    };

    applyHeight();

    const observer = new ResizeObserver(() => {
      applyHeight();
    });

    observer.observe(node);

    return {
      destroy() {
        observer.disconnect();
        cardElement.style.removeProperty('--record-preview-height');
      }
    };
  }
</script>

<section class="record-library" aria-label={isZh ? '截图记录列表' : 'Screenshot record list'}>
  <div class="record-library-head">
    <div class="record-library-copy">
      <p class="record-library-eyebrow">{isZh ? '截图记录' : 'Screenshot Records'}</p>
      <h2>{isZh ? '对局截图' : 'Match Screenshots'}</h2>
    </div>

    <div class="record-library-actions">
      <button type="button" class="refresh-button" on:click={refreshRecords} disabled={loading}>
        {loading ? (isZh ? '刷新中...' : 'Refreshing...') : isZh ? '刷新列表' : 'Refresh'}
      </button>
    </div>
  </div>

  <div class="record-list-shell">
    {#if records.length === 0}
      <p class="record-empty">
        {isZh
          ? '当前还没有可显示的截图记录。点击刷新会重新从 SQLite 读取。'
          : 'No screenshot records are available yet. Refresh to read SQLite again.'}
      </p>
    {:else}
      <div class="record-list">
        {#each records as record}
          {@const wins =
            typeof record.wins === 'number' && Number.isFinite(record.wins) ? record.wins : null}
          {@const battles =
            typeof record.battle_count === 'number' && Number.isFinite(record.battle_count)
              ? record.battle_count
              : null}
          {@const winsBadgeSrc = getWinsBadgeAsset(wins, battles)}
          {@const secondaryBadgeSrc = resolveSecondaryBadgeSrc(record)}
          {@const imageSrc = resolveRecordImageSrc(record)}
          <article class="record-row">
            <div class="record-card">
              <div class="record-strip-shell">
                <div class={`record-badges mode-${displayMode}`}>
                  <div class="badge-shell">
                    {#if winsBadgeSrc}
                      <img
                        class="metric-badge-svg"
                        src={winsBadgeSrc}
                        alt={`${wins ?? 'unknown'} wins`}
                        loading="lazy"
                      />
                    {/if}
                  </div>
                  <div class={`badge-shell badge-shell-secondary mode-${displayMode}`}>
                    {#if secondaryBadgeSrc}
                      <img
                        class="metric-badge-svg"
                        src={secondaryBadgeSrc}
                        alt={displayMode === 'current'
                          ? `${battles ?? 'unknown'} battles`
                          : `${record.title || (isZh ? '未知英雄' : 'Unknown hero')} badge`}
                        loading="lazy"
                      />
                    {/if}
                  </div>
                </div>

                <div class="record-preview">
                  <div class="visual-frame">
                    <div class="visual-stage" use:syncRecordPreviewHeight>
                      {#if imageSrc}
                        <img
                          class="visual-image"
                          src={imageSrc}
                          alt={`${record.title || (isZh ? '未知英雄' : 'Unknown hero')} screenshot`}
                          loading="lazy"
                        />
                      {:else}
                        <div class="record-preview-empty">
                          {previewBaseUrl
                            ? isZh
                              ? '加载预览中…'
                              : 'Loading preview...'
                            : isZh
                              ? '预览暂不可用'
                              : 'Preview unavailable'}
                        </div>
                      {/if}
                    </div>
                  </div>
                </div>

                <div class="record-actions">
                  <button
                    type="button"
                    class="record-action"
                    on:click={() => revealRecordImage(record.id)}
                  >
                    {isZh ? '打开' : 'Open'}
                  </button>
                  <button
                    type="button"
                    class="record-action record-action-danger"
                    on:click={() => deleteRecord(record.id)}
                    disabled={deletingRecordIds.has(record.id)}
                  >
                    {deletingRecordIds.has(record.id)
                      ? isZh
                        ? '删除中'
                        : 'Deleting'
                      : isZh
                        ? '删除'
                        : 'Delete'}
                  </button>
                </div>
              </div>
            </div>
          </article>
        {/each}
      </div>
    {/if}
  </div>
</section>

<style>
  .record-library {
    display: grid;
    gap: 0.85rem;
    padding: 1.05rem;
    border-radius: 3px;
    border: 1px solid rgba(185, 134, 58, 0.14);
    background:
      radial-gradient(circle at top left, rgba(var(--color-warm-rgb), 0.05), transparent 42%),
      linear-gradient(180deg, rgba(20, 12, 6, 0.96), rgba(12, 7, 4, 0.94));
    box-shadow:
      0 8px 28px rgba(0, 0, 0, 0.3),
      inset 0 0 0 1px rgba(var(--color-warm-rgb), 0.04);
  }

  .record-library-head {
    display: flex;
    justify-content: space-between;
    gap: 1rem;
    align-items: flex-start;
  }

  .record-library-copy {
    display: grid;
    gap: 0.2rem;
  }

  .record-library-eyebrow {
    margin: 0;
    font-family: 'Cinzel', serif;
    font-size: 0.5rem;
    letter-spacing: 0.24em;
    text-transform: uppercase;
    color: rgba(var(--color-accent-rgb), 0.52);
  }

  .record-library-copy h2,
  .record-empty,
  .record-preview-empty {
    margin: 0;
  }

  .record-library-copy h2 {
    font-family: 'Cinzel', serif;
    font-size: 0.82rem;
    letter-spacing: 0.08em;
    text-transform: uppercase;
    color: rgba(232, 220, 194, 0.92);
  }

  .record-empty,
  .record-preview-empty {
    font-size: 0.78rem;
    line-height: 1.55;
    color: rgba(208, 188, 150, 0.74);
  }

  .record-library-actions {
    display: flex;
    align-items: flex-start;
    justify-content: flex-end;
    gap: 0.65rem;
    flex-wrap: wrap;
  }

  .record-list-shell {
    padding: 0.8rem 0.9rem;
    border-radius: 2px;
    border: 1px solid rgba(176, 126, 52, 0.12);
    background: rgba(10, 7, 4, 0.58);
  }

  .record-list {
    display: grid;
    gap: 0.7rem;
    max-height: 25rem;
    overflow-y: auto;
    padding-right: 0.2rem;
  }

  .record-row {
    min-width: 0;
  }

  .record-card {
    --record-preview-height: clamp(6.2rem, 10vw, 8.15rem);
    display: flex;
    justify-content: center;
    min-width: 0;
    padding: 0.16rem;
    border: 1px solid rgba(178, 134, 64, 0.14);
    border-radius: 9px;
    overflow: hidden;
    background:
      linear-gradient(180deg, rgba(15, 11, 7, 0.95), rgba(7, 5, 3, 0.95)),
      radial-gradient(circle at top right, rgba(217, 170, 88, 0.05), transparent 42%);
    box-shadow: 0 8px 20px rgba(0, 0, 0, 0.14);
  }

  .record-strip-shell {
    display: grid;
    grid-template-columns: max-content max-content auto;
    column-gap: 0;
    align-items: center;
    justify-content: center;
    min-width: 0;
    width: fit-content;
    max-width: 100%;
  }

  .record-badges {
    display: grid;
    gap: 0;
    align-self: center;
    height: var(--record-preview-height);
    min-width: 0;
  }

  .record-badges.mode-current,
  .record-badges.mode-hero {
    grid-template-rows: 1fr 1fr;
    aspect-ratio: 1 / 2;
  }

  .record-badges.mode-herohalf {
    grid-template-rows: 2fr 1fr;
    aspect-ratio: 2 / 3;
  }

  .badge-shell {
    display: block;
    width: 100%;
    height: 100%;
    min-width: 0;
    min-height: 0;
    overflow: hidden;
    border-radius: 0.32rem;
  }

  .metric-badge-svg {
    display: block;
    width: 100%;
    height: 100%;
  }

  .record-preview {
    display: flex;
    align-items: center;
    min-width: 0;
    align-self: center;
  }

  .visual-frame {
    width: fit-content;
    max-width: 100%;
    display: flex;
    align-items: center;
    min-width: 0;
  }

  .visual-stage {
    display: flex;
    align-items: center;
    overflow: hidden;
    width: fit-content;
    max-width: 100%;
    border-radius: 8px;
    background: rgba(3, 3, 3, 0.65);
  }

  .visual-image {
    display: block;
    width: auto;
    height: auto;
    max-width: 100%;
    object-fit: contain;
  }

  .record-preview-empty {
    display: grid;
    place-items: center;
    width: 100%;
    height: 100%;
  }

  .refresh-button,
  .record-action {
    width: auto;
    min-height: 2.2rem;
    padding: 0.55rem 0.8rem;
    border-radius: 2px;
    border: 1px solid rgba(183, 132, 57, 0.16);
    background: rgba(192, 138, 54, 0.08);
    color: rgba(240, 227, 198, 0.82);
    font-family: 'Cinzel', serif;
    font-size: 0.58rem;
    letter-spacing: 0.14em;
    text-transform: uppercase;
    cursor: pointer;
  }

  .refresh-button:disabled,
  .record-action:disabled {
    opacity: 0.45;
    cursor: not-allowed;
  }

  .record-action {
    width: 100%;
    min-width: 4.1rem;
    min-height: 1.75rem;
    padding: 0.3rem 0.46rem;
    font-size: 0.52rem;
    line-height: 1.15;
  }

  .record-actions {
    display: grid;
    align-self: center;
    align-content: center;
    gap: 0.35rem;
    width: 4.25rem;
    margin-left: 0.35rem;
  }

  .record-action-danger {
    border-color: rgba(201, 105, 84, 0.22);
    background: rgba(114, 28, 20, 0.12);
    color: rgba(255, 216, 206, 0.9);
  }

  @media (max-width: 900px) {
    .record-strip-shell {
      grid-template-columns: max-content minmax(0, 1fr) auto;
    }
  }

  @media (max-width: 640px) {
    .record-card {
      --record-preview-height: clamp(5.4rem, 24vw, 6.5rem);
    }

    .record-library-head {
      display: grid;
    }

    .record-library-actions {
      justify-content: space-between;
    }

    .record-strip-shell {
      grid-template-columns: max-content minmax(0, 1fr) auto;
    }

    .record-badges {
      justify-content: stretch;
    }

    .record-action {
      min-width: 4.4rem;
    }
  }
</style>
