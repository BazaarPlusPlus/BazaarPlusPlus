<script lang="ts">
  import { openUrl } from '@tauri-apps/plugin-opener';
  import { getTutorialUrl } from '$lib/config/endpoints';
  import { messages } from '$lib/i18n';
  import { locale } from '$lib/locale';
  import type { EnvironmentInfo } from '$lib/types';
  import type { ActionBusy } from '$lib/installer/state';

  export let env: EnvironmentInfo | null;
  export let modInstalled: boolean;
  export let versionMismatch: boolean;
  export let bundledBppVersion: string | null;
  export let installedBppVersion: string | null;
  export let actionBusy: ActionBusy;
  export let t: (
    key: keyof typeof messages.en,
    params?: Record<string, string | number>
  ) => string;

  async function openTutorial() {
    await openUrl(getTutorialUrl($locale));
  }
</script>

<div
  class="step"
  class:step-found={modInstalled && !versionMismatch}
  class:step-error={versionMismatch}
>
  <div class="step-index" aria-hidden="true">I</div>
  <div class="step-body">
    <span class="step-title">
      {t('stepBpp')}
      {#if versionMismatch}
        <span class="tag tag-danger"
          >{$locale === 'zh' ? '版本不一致' : 'Version mismatch'}</span
        >
      {:else if actionBusy === 'detect'}
        <span class="tag">{t('statusChecking')}</span>
      {:else if !modInstalled}
        <span class="tag tag-warn">{t('statusNotInstalled')}</span>
      {/if}
    </span>
    <div class="bpp-detail-row">
      <div class="bpp-detail-content">
        {#if versionMismatch}
          <div class="mismatch-summary">
            <p class="detail-line detail-muted">
              {$locale === 'zh'
                ? '已安装版本和安装器版本不同，请点击下方“需要重新安装”按钮完成重新安装'
                : 'The installed version differs from the bundled one. Select the Reinstall Required button below to reinstall.'}
            </p>
          </div>
          <div class="mismatch-versions">
            <span class="mismatch-version">
              <span class="mismatch-version-label"
                >{$locale === 'zh' ? '本地已安装' : 'Installed'}</span
              >
              <span class="mismatch-version-value">v{installedBppVersion}</span>
            </span>
            <span class="mismatch-version">
              <span class="mismatch-version-label"
                >{$locale === 'zh' ? '安装器版本' : 'Installer bundle'}</span
              >
              <span class="mismatch-version-value">v{bundledBppVersion}</span>
            </span>
          </div>
        {:else if modInstalled}
          <div class="mismatch-versions">
            <span class="mismatch-version mismatch-version-ok">
              <span class="mismatch-version-label"
                >{$locale === 'zh' ? '本地已安装' : 'Installed'}</span
              >
              <span class="mismatch-version-value">v{env?.bpp_version}</span>
            </span>
          </div>
          <p class="detail-line detail-muted">
            {$locale === 'zh'
              ? 'BazaarPlusPlus 当前已处于最新状态'
              : 'BazaarPlusPlus is already up to date'}
          </p>
        {:else}
          <p class="detail-line detail-muted">{t('detectInstalledHint')}</p>
        {/if}
      </div>
      <button class="tutorial-btn" type="button" onclick={openTutorial}>
        {$locale === 'zh' ? '查看教程' : 'View Tutorial'}
      </button>
    </div>
  </div>
</div>

<style>
  .bpp-detail-row {
    display: grid;
    grid-template-columns: minmax(0, 1fr) auto;
    align-items: center;
    gap: 0.85rem;
    min-width: 0;
  }

  .bpp-detail-content {
    display: grid;
    gap: 0.58rem;
    min-width: 0;
  }

  .tutorial-btn {
    justify-self: end;
    min-height: 2.3rem;
    padding: 0.54rem 0.9rem;
    font-family: 'Cinzel', serif;
    font-size: 0.54rem;
    letter-spacing: 0.14em;
    text-transform: uppercase;
    color: rgba(200, 155, 72, 0.82);
    background: rgba(var(--color-accent-rgb), 0.06);
    border: 1px solid rgba(180, 130, 48, 0.2);
    border-radius: 2px;
    cursor: pointer;
    transition:
      background 0.15s ease,
      color 0.15s ease,
      border-color 0.15s ease,
      box-shadow 0.15s ease;
    white-space: nowrap;
  }

  .tutorial-btn:hover {
    color: rgba(220, 180, 100, 0.95);
    background: rgba(var(--color-accent-rgb), 0.12);
    border-color: rgba(var(--color-accent-rgb), 0.34);
    box-shadow: 0 0 16px rgba(var(--color-accent-rgb), 0.08);
  }

  .tutorial-btn:focus-visible {
    outline: 2px solid rgba(var(--color-warm-rgb), 0.9);
    outline-offset: 2px;
  }

  .mismatch-summary {
    display: flex;
    gap: 0.55rem;
  }

  .mismatch-versions {
    display: flex;
    flex-direction: column;
    gap: 0.45rem;
    min-width: 0;
    align-items: flex-start;
  }

  .mismatch-version {
    display: inline-flex;
    align-items: center;
    gap: 0.45rem;
    min-width: 0;
    padding: 0.28rem 0.48rem;
    border: 1px solid rgba(191, 104, 81, 0.14);
    border-radius: 999px;
    background: rgba(191, 104, 81, 0.06);
  }

  .mismatch-version-ok {
    border-color: rgba(80, 180, 120, 0.24);
    background: rgba(80, 180, 120, 0.1);
    box-shadow: 0 0 0 1px rgba(80, 180, 120, 0.04) inset;
  }

  .mismatch-version-label {
    color: rgba(214, 182, 126, 0.62);
    font-size: 0.63rem;
    white-space: nowrap;
  }

  .mismatch-version-ok .mismatch-version-label {
    color: rgba(156, 214, 179, 0.78);
  }

  .mismatch-version-value {
    color: rgba(235, 223, 198, 0.86);
    font-family: 'Fira Code', monospace;
    font-size: 0.66rem;
    white-space: nowrap;
  }

  .mismatch-version-ok .mismatch-version-value {
    color: rgba(216, 244, 228, 0.92);
  }

  @media (max-width: 520px) {
    .bpp-detail-row {
      grid-template-columns: minmax(0, 1fr);
    }
  }
</style>
