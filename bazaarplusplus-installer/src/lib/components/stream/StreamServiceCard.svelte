<script lang="ts">
  import { locale } from '$lib/locale';
  import type { StreamPageState } from '$lib/stream/state';
  import type {
    StreamDbPathInfo,
    StreamOverlayDisplayMode,
    StreamServiceStatus
  } from '$lib/types';

  export let status: StreamServiceStatus;
  export let pageState: StreamPageState;
  export let busy = false;
  export let savingDisplayMode = false;
  export let countBefore = 0;
  export let countAfter = 0;
  export let dbPathInfo: StreamDbPathInfo = { found: false, path: null };
  export let displayMode: StreamOverlayDisplayMode = 'current';
  export let importingCropCode = false;
  export let previewUrl: string | null = null;
  export let cropCodeInput = '';
  export let cropCodeMessage = '';
  export let copyMessage = '';
  export let copyMessageTone: 'success' | 'error' | null = null;
  export let overviewStartLabel = '';
  export let overviewHeroLabel = '';
  export let canStepEarlier = false;
  export let canStepLater = false;
  export let onStart: () => void | Promise<void>;
  export let onStop: () => void | Promise<void>;
  export let onCopyUrl: () => void | Promise<void>;
  export let onOpenPreview: () => void | Promise<void>;
  export let onOpenCalibration: () => void | Promise<void>;
  export let onStepEarlier: () => void | Promise<void>;
  export let onStepLater: () => void | Promise<void>;
  export let onDisplayModeChange: (mode: StreamOverlayDisplayMode) => void | Promise<void>;
  export let onCropCodeInput: (value: string) => void;
  export let onImportCropCode: () => void | Promise<void>;

  let importPanelOpen = false;
  $: isZh = $locale === 'zh';
  $: eyebrow = isZh ? '直播服务' : 'Live Service';
  $: title = status.running
    ? isZh
      ? '直播服务运行中'
      : 'Stream service is running'
    : isZh
      ? '直播服务未启动'
      : 'Stream service is stopped';
  $: statusBadge = status.running ? (isZh ? '运行中' : 'Live') : isZh ? '空闲' : 'Idle';
  $: utilityActions = [
    {
      label: copyMessage || (isZh ? '复制 OBS 地址' : 'Copy OBS URL'),
      state: copyMessageTone,
      disabled: !pageState.canCopyUrl,
      action: onCopyUrl
    },
    {
      label: isZh ? '打开预览页' : 'Open Preview',
      state: null,
      disabled: !pageState.canOpenPreview,
      action: onOpenPreview
    },
    {
      label: isZh ? '打开校准页' : 'Open Calibration',
      state: null,
      disabled: !pageState.canOpenPreview,
      action: onOpenCalibration
    },
    {
      label: importPanelOpen
        ? isZh
          ? '收起裁切代码'
          : 'Hide Crop Code'
        : isZh
          ? '导入裁切代码'
          : 'Import Crop Code',
      state: null,
      disabled: busy,
      action: () => {
        importPanelOpen = !importPanelOpen;
      }
    }
  ];
  $: toggleActionLabel = busy
    ? isZh
      ? '处理中...'
      : 'Working...'
    : status.running
      ? isZh
        ? '关闭服务'
        : 'Stop Service'
      : isZh
        ? '启动服务'
        : 'Start Service';
  $: toggleAction = status.running ? onStop : onStart;
  $: displayModeOptions = [
    { value: 'current' as const, label: isZh ? '战斗场数' : 'Battle Count' },
    { value: 'hero' as const, label: isZh ? '完整英雄' : 'Full Hero' },
    { value: 'herohalf' as const, label: isZh ? '半高英雄' : 'Half-Height Hero' }
  ];
</script>

<section class="card">
  <div class="heading-row">
    <div class="heading-copy">
      <p class="eyebrow">{eyebrow}</p>
      <h2>{title}</h2>
    </div>

    <div class="badge-group">
      {#if status.running}
        <span class="count-badge count-after" title={isZh ? '开播后新增记录' : 'Records captured since stream started'}>
          {isZh ? `开播后 ${countAfter}` : `+${countAfter} After`}
        </span>
        <span class="count-badge count-before" title={isZh ? '开播前已有记录' : 'Records captured before stream started'}>
          {isZh ? `开播前 ${countBefore}` : `${countBefore} Before`}
        </span>
      {/if}
      <span
        class="count-badge"
        class:db-tag-found={dbPathInfo.found}
        class:db-tag-missing={!dbPathInfo.found}
        title={dbPathInfo.path ?? ''}
      >
        {dbPathInfo.found ? (isZh ? 'DB 已找到' : 'DB OK') : (isZh ? 'DB 未找到' : 'DB Missing')}
      </span>
      <span class:online={status.running} class="badge">{statusBadge}</span>
    </div>
  </div>

  {#if previewUrl}
    <div class="url-shell">
      <p class="detail-label">{isZh ? 'OBS 浏览器源地址' : 'OBS browser source URL'}</p>
      <div class="url-box">{previewUrl}</div>
    </div>
  {/if}

  {#if status.last_error}
    <p class="error">{status.last_error}</p>
  {/if}

  <div class="overview-shell">
    <div class="overview-copy">
      <div class="overview-detail">
        <div>
          <p class="detail-label">{isZh ? 'Overview 起始时间' : 'Overview Start Time'}</p>
          <p class="overview-value">{overviewStartLabel}</p>
        </div>

        <div>
          <p class="detail-label">{isZh ? '英雄' : 'Hero'}</p>
          <p class="overview-value">{overviewHeroLabel}</p>
        </div>
      </div>
    </div>

    <div class="overview-controls">
      <div class="step-actions">
        <button
          class="secondary icon-button"
          disabled={!canStepEarlier || busy}
          on:click={onStepEarlier}
        >
          ↑
        </button>
        <button
          class="secondary icon-button"
          disabled={!canStepLater || busy}
          on:click={onStepLater}
        >
          ↓
        </button>
      </div>
    </div>
  </div>

  <div class="mode-shell">
    <p class="detail-label">{isZh ? 'Overlay 显示模式' : 'Overlay Display Mode'}</p>
    <div class="mode-picker" role="radiogroup" aria-label={isZh ? 'Overlay 显示模式' : 'Overlay display mode'}>
      {#each displayModeOptions as option}
        <button
          class="mode-chip"
          class:is-active={displayMode === option.value}
          disabled={busy || savingDisplayMode}
          aria-pressed={displayMode === option.value}
          on:click={() => onDisplayModeChange(option.value)}
        >
          {option.label}
        </button>
      {/each}
    </div>
  </div>

  <div class="actions">
    <button class:running={status.running} class="primary toggle-button" disabled={busy} on:click={toggleAction}>
      {toggleActionLabel}
    </button>

    <div class="utility-shell">
      <p class="detail-label">{isZh ? '快捷工具' : 'Quick tools'}</p>

      <div class="utility-actions">
        {#each utilityActions as item}
          <button
            class="secondary"
            class:is-success={item.state === 'success'}
            class:is-error={item.state === 'error'}
            disabled={item.disabled}
            on:click={item.action}
          >
            {item.label}
          </button>
        {/each}
      </div>
    </div>

  </div>

  {#if importPanelOpen}
    <div class="advanced-shell">
      <p class="detail-label">{isZh ? '裁切代码' : 'Crop code'}</p>
      <p class="advanced-copy">
        {isZh
          ? '只在你已经拿到外部裁切代码时使用；保存后，overlay 会在下次刷新时应用。'
          : 'Use this only when you already have an external crop code. The overlay applies it on the next refresh.'}
      </p>
    </div>

    <label class="code-block">
      <span class="code-label">
        {isZh ? '粘贴 Base64 裁切代码' : 'Import Base64 Crop Code'}
      </span>
      <textarea
        value={cropCodeInput}
        rows="4"
        spellcheck="false"
        on:input={(event) => onCropCodeInput(event.currentTarget.value)}
      ></textarea>
    </label>

    <div class="import-row">
      <button disabled={busy || importingCropCode || !cropCodeInput.trim()} on:click={onImportCropCode}
        >{isZh ? '应用裁切代码' : 'Apply Crop Code'}</button
      >
      {#if cropCodeMessage}
        <p class="import-message">{cropCodeMessage}</p>
      {/if}
    </div>
  {/if}
</section>

<style>
  .card {
    display: grid;
    gap: 0.9rem;
    padding: 1.15rem;
    border-radius: 3px;
    border: 1px solid rgba(185, 134, 58, 0.14);
    background: linear-gradient(
      175deg,
      rgba(30, 18, 8, 0.9),
      rgba(12, 8, 4, 0.84)
    );
    box-shadow:
      0 0 0 1px rgba(var(--color-accent-rgb), 0.05) inset,
      0 18px 44px rgba(0, 0, 0, 0.24);
  }

  .heading-row {
    display: flex;
    justify-content: space-between;
    gap: 1rem;
    align-items: flex-start;
  }

  .badge-group {
    display: flex;
    flex-wrap: wrap;
    gap: 0.45rem;
    align-items: flex-start;
    justify-content: flex-end;
    flex: 0 0 auto;
  }

  .count-badge {
    display: inline-flex;
    align-items: center;
    padding: 0.4rem 0.6rem;
    border-radius: 2px;
    font-family: 'Cinzel', serif;
    font-size: 0.55rem;
    letter-spacing: 0.14em;
    text-transform: uppercase;
    white-space: nowrap;
  }

  .count-after {
    border: 1px solid rgba(130, 200, 120, 0.22);
    background: rgba(80, 160, 70, 0.1);
    color: rgba(180, 230, 170, 0.85);
  }

  .count-before {
    border: 1px solid rgba(190, 137, 59, 0.16);
    background: rgba(192, 138, 54, 0.06);
    color: rgba(220, 200, 160, 0.7);
  }

  .heading-copy {
    display: grid;
    gap: 0.2rem;
  }

  .eyebrow {
    margin: 0 0 0.2rem;
    font-family: 'Cinzel', serif;
    font-size: 0.58rem;
    letter-spacing: 0.22em;
    text-transform: uppercase;
    color: rgba(212, 160, 78, 0.62);
  }

  h2,
  .error,
  .detail-label {
    margin: 0;
  }

  h2 {
    font-size: 1.22rem;
    color: #f0e2bf;
  }

  .badge {
    flex: 0 0 auto;
    min-width: 6.5rem;
    padding: 0.4rem 0.7rem;
    border-radius: 2px;
    border: 1px solid rgba(190, 137, 59, 0.16);
    font-family: 'Cinzel', serif;
    font-size: 0.58rem;
    letter-spacing: 0.18em;
    text-transform: uppercase;
    color: rgba(231, 220, 194, 0.7);
  }

  .badge.online {
    border-color: rgba(211, 159, 77, 0.3);
    color: #f5dfa8;
    background: rgba(192, 136, 52, 0.12);
  }

  .db-tag-found {
    border-color: rgba(120, 190, 110, 0.22);
    background: rgba(60, 140, 50, 0.09);
    color: rgba(170, 220, 160, 0.82);
  }

  .db-tag-missing {
    border-color: rgba(214, 118, 104, 0.24);
    background: rgba(132, 48, 37, 0.14);
    color: rgba(255, 190, 175, 0.84);
  }

  .url-shell {
    display: grid;
    gap: 0.32rem;
    padding: 0.8rem 0.9rem;
    border-radius: 2px;
    border: 1px solid rgba(176, 126, 52, 0.12);
    background: rgba(10, 7, 4, 0.58);
  }

  .detail-label {
    font-size: 0.68rem;
    letter-spacing: 0.08em;
    text-transform: uppercase;
    color: rgba(213, 188, 145, 0.74);
  }

  .url-box {
    color: #eccf92;
    font-family: 'Fira Code', monospace;
    font-size: 0.8rem;
    word-break: break-all;
  }

  .error {
    color: #ffb8a1;
  }

  .actions {
    display: grid;
    gap: 0.75rem;
  }

  .utility-shell,
  .advanced-shell,
  .overview-shell,
  .mode-shell {
    display: grid;
    gap: 0.45rem;
    padding: 0.75rem 0.85rem;
    border-radius: 2px;
    border: 1px solid rgba(176, 126, 52, 0.12);
    background: rgba(9, 6, 4, 0.52);
  }

  .utility-actions {
    display: grid;
    grid-template-columns: repeat(2, minmax(0, 1fr));
    gap: 0.55rem;
  }

  .mode-picker {
    display: flex;
    flex-wrap: wrap;
    gap: 0.45rem;
  }

  .overview-shell {
    grid-template-columns: minmax(0, 1fr) auto;
    gap: 0.85rem;
    align-items: start;
  }

  .overview-copy,
  .overview-controls,
  .step-actions {
    display: grid;
    gap: 0.35rem;
  }

  .overview-detail {
    display: grid;
    grid-template-columns: repeat(2, minmax(0, 1fr));
    gap: 0.75rem;
  }

  .overview-value {
    margin: 0;
    font-size: 0.94rem;
    color: #f0e2bf;
  }

  .overview-controls {
    grid-template-columns: auto;
    align-items: start;
    justify-items: end;
  }

  .step-actions {
    grid-template-columns: repeat(2, minmax(2.4rem, auto));
    gap: 0.45rem;
  }

  .icon-button {
    width: 2.6rem;
    min-width: 2.6rem;
    padding: 0;
    font-size: 0.9rem;
    letter-spacing: 0;
  }

  .advanced-copy {
    margin: 0;
    color: rgba(215, 197, 161, 0.68);
    line-height: 1.45;
    font-size: 0.8rem;
  }

  .code-block {
    display: grid;
    gap: 0.35rem;
  }

  .code-label,
  .import-message {
    margin: 0;
  }

  .code-label {
    font-size: 0.68rem;
    letter-spacing: 0.08em;
    text-transform: uppercase;
    color: rgba(213, 188, 145, 0.74);
  }

  textarea {
    width: 100%;
    min-height: 5.8rem;
    padding: 0.7rem 0.8rem;
    border-radius: 2px;
    border: 1px solid rgba(183, 132, 57, 0.16);
    background: rgba(8, 6, 4, 0.82);
    color: #eccf92;
    font-family: 'Fira Code', monospace;
    font-size: 0.76rem;
    resize: vertical;
    box-sizing: border-box;
  }

  .import-row {
    display: grid;
    gap: 0.45rem;
  }

  .import-message {
    font-size: 0.76rem;
    color: rgba(231, 220, 196, 0.72);
  }

  button.mode-chip {
    width: auto;
    min-width: 0;
    min-height: 2.15rem;
    padding: 0.5rem 0.85rem;
    border-radius: 999px;
    font-size: 0.58rem;
    background: rgba(192, 138, 54, 0.06);
    color: rgba(240, 227, 198, 0.72);
  }

  button.mode-chip.is-active {
    border-color: rgba(216, 164, 82, 0.3);
    background: linear-gradient(
      180deg,
      rgba(199, 145, 58, 0.2),
      rgba(116, 68, 24, 0.22)
    );
    color: var(--color-soft-gold);
  }

  button {
    width: 100%;
    min-height: 2.4rem;
    padding: 0.65rem 0.9rem;
    border-radius: 2px;
    border: 1px solid rgba(183, 132, 57, 0.16);
    background: rgba(192, 138, 54, 0.08);
    color: rgba(240, 227, 198, 0.82);
    font-family: 'Cinzel', serif;
    font-size: 0.62rem;
    letter-spacing: 0.14em;
    text-transform: uppercase;
    cursor: pointer;
  }

  button.secondary,
  .toggle-button.running {
    background: rgba(192, 138, 54, 0.05);
  }

  button.secondary.is-success {
    border-color: rgba(216, 164, 82, 0.3);
    background: linear-gradient(
      180deg,
      rgba(199, 145, 58, 0.2),
      rgba(116, 68, 24, 0.22)
    );
    color: var(--color-soft-gold);
  }

  button.secondary.is-error {
    border-color: rgba(214, 118, 104, 0.28);
    background: rgba(132, 48, 37, 0.2);
    color: #ffcbc0;
  }

  button.primary {
    background: linear-gradient(
      180deg,
      rgba(199, 145, 58, 0.28),
      rgba(116, 68, 24, 0.32)
    );
    border-color: rgba(216, 164, 82, 0.3);
  }

  button:disabled {
    opacity: 0.45;
    cursor: not-allowed;
  }

  @media (max-width: 520px) {
    .overview-shell,
    .overview-controls,
    .overview-detail {
      grid-template-columns: 1fr;
    }

    .utility-actions {
      grid-template-columns: 1fr;
    }
  }
</style>
