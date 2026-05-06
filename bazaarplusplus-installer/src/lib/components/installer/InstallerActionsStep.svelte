<script lang="ts">
  import { openUrl } from '@tauri-apps/plugin-opener';
  import { messages } from '$lib/i18n';
  import { locale } from '$lib/locale';
  import type { EnvironmentInfo } from '$lib/types';
  import type { ActionBusy, StepState } from '$lib/installer/state';

  export let env: EnvironmentInfo | null;
  export let dotnetState: StepState;
  export let modInstalled: boolean;
  export let versionMismatch: boolean;
  export let isBusy: boolean;
  export let actionBusy: ActionBusy;
  export let canInstall: boolean;
  export let canLaunchGame: boolean;
  export let dotnetDownloadUrl: string;
  export let t: (
    key: keyof typeof messages.en,
    params?: Record<string, string | number>
  ) => string;
  export let onRequestInstall: () => void | Promise<void>;
  export let onRepair: () => void | Promise<void>;
  export let onUninstall: () => void | Promise<void>;
  export let onLaunchGame: () => void | Promise<void>;

  let actionMenuOpen = false;

  function toggleActionMenu() {
    actionMenuOpen = !actionMenuOpen;
  }

  async function handleUninstall() {
    actionMenuOpen = false;
    await onUninstall();
  }
</script>

<div class="step step-install">
  <div class="step-index" aria-hidden="true">III</div>
  <div class="step-body">
    <div class="step-heading">
      <span class="step-title">{t('stepActions')}</span>

      {#if dotnetState === 'not_found'}
        <button
          class="runtime-chip runtime-chip-button"
          onclick={() => openUrl(dotnetDownloadUrl)}
          type="button"
        >
          {t('runtimeDownload')}
        </button>
      {:else}
        <div class="runtime-chip" aria-live="polite">
          <span class="runtime-chip-label">.NET</span>
          <span class="runtime-chip-value">
            {#if dotnetState === 'found'}
              {env?.dotnet_version ?? 'OK'}
            {:else if actionBusy === 'detect'}
              {t('statusChecking')}
            {:else}
              ...
            {/if}
          </span>
        </div>
      {/if}
    </div>

    <div class="action-row">
      <div class="action-primary">
        <button
          class="install-btn"
          class:install-btn-danger={versionMismatch}
          disabled={!canInstall}
          onclick={onRequestInstall}
          type="button"
        >
          {#if actionBusy === 'install'}
            <span class="spinner dark" aria-hidden="true"></span>
            {t('actionInstalling')}
          {:else if versionMismatch}
            {$locale === 'zh' ? '⚠ 需要重新安装' : '⚠ Reinstall Required'}
          {:else if modInstalled}
            ✦ {t('actionReinstall')}
          {:else}
            ✦ {t('actionInstall')}
          {/if}
        </button>
        <button
          class="secondary-btn repair-btn"
          type="button"
          onclick={onRepair}
          disabled={isBusy}
        >
          {#if actionBusy === 'repair'}
            {t('actionRepairing')}
          {:else}
            {t('actionRepair')}
          {/if}
        </button>
        <div class="menu-wrap">
          <button
            class="secondary-btn menu-trigger"
            type="button"
            onclick={toggleActionMenu}
            disabled={isBusy}
            aria-expanded={actionMenuOpen}
          >
            ...
          </button>
          {#if actionMenuOpen}
            <div class="action-menu">
              <button
                class="menu-item"
                type="button"
                onclick={handleUninstall}
                disabled={isBusy}
              >
                {#if actionBusy === 'uninstall'}
                  {t('actionUninstalling')}
                {:else}
                  {t('actionUninstall')}
                {/if}
              </button>
            </div>
          {/if}
        </div>
      </div>
    </div>

    <button
      class="secondary-btn launch-btn"
      type="button"
      onclick={onLaunchGame}
      disabled={!canLaunchGame}
    >
      {$locale === 'zh' ? '启动游戏' : 'Launch Game'}
    </button>
  </div>
</div>

<style>
  .runtime-chip {
    flex: 0 0 auto;
    min-width: 0;
    padding: 0.28rem 0.52rem;
    border: 1px solid rgba(92, 146, 176, 0.16);
    border-radius: 999px;
    background: rgba(92, 146, 176, 0.06);
    display: inline-flex;
    align-items: center;
    gap: 0.42rem;
    color: rgba(150, 196, 224, 0.88);
  }

  .runtime-chip-button {
    color: rgba(128, 176, 206, 0.82);
    border-color: rgba(100, 160, 220, 0.22);
    background: rgba(100, 160, 220, 0.08);
    cursor: pointer;
    transition:
      background 0.15s ease,
      color 0.15s ease,
      border-color 0.15s ease;
  }

  .runtime-chip-button:hover {
    background: rgba(100, 160, 220, 0.14);
    color: rgba(156, 204, 234, 0.94);
    border-color: rgba(100, 160, 220, 0.32);
  }

  .runtime-chip-button:focus-visible {
    outline: 2px solid rgba(var(--color-warm-rgb), 0.9);
    outline-offset: 2px;
  }

  .runtime-chip-label {
    font-family: 'Cinzel', serif;
    font-size: 0.46rem;
    letter-spacing: 0.12em;
    text-transform: uppercase;
    color: rgba(196, 168, 120, 0.72);
  }

  .runtime-chip-value {
    font-family: 'Fira Code', monospace;
    font-size: 0.58rem;
    letter-spacing: 0;
    text-transform: none;
    color: currentColor;
  }

  .action-row {
    display: grid;
    gap: 0.75rem;
  }

  .action-primary {
    min-width: 0;
    display: flex;
    flex-wrap: wrap;
    align-items: stretch;
    gap: 0.5rem;
    position: relative;
  }

  .secondary-btn {
    padding: 0.62rem 0.84rem;
    font-family: 'Cinzel', serif;
    font-size: 0.54rem;
    letter-spacing: 0.14em;
    text-transform: uppercase;
    color: rgba(200, 155, 72, 0.78);
    background: rgba(var(--color-accent-rgb), 0.06);
    border: 1px solid rgba(180, 130, 48, 0.18);
    border-radius: 2px;
    cursor: pointer;
    transition:
      background 0.15s ease,
      color 0.15s ease,
      border-color 0.15s ease;
    white-space: nowrap;
    display: inline-flex;
    align-items: center;
    justify-content: center;
    gap: 0.45rem;
  }

  .secondary-btn:hover:not(:disabled) {
    background: rgba(var(--color-accent-rgb), 0.12);
    color: rgba(220, 180, 100, 0.95);
    border-color: rgba(var(--color-accent-rgb), 0.34);
  }

  .secondary-btn:disabled {
    opacity: 0.35;
    cursor: not-allowed;
  }

  .repair-btn {
    white-space: nowrap;
  }

  .launch-btn {
    width: 100%;
  }

  .menu-wrap {
    position: relative;
    flex-shrink: 0;
  }

  .menu-trigger {
    min-width: 42px;
    height: 100%;
    padding-left: 0.7rem;
    padding-right: 0.7rem;
  }

  .action-menu {
    position: absolute;
    right: 0;
    top: calc(100% + 0.35rem);
    min-width: 140px;
    padding: 0.35rem;
    border: 1px solid rgba(180, 130, 48, 0.18);
    border-radius: 3px;
    background: rgba(18, 11, 5, 0.96);
    box-shadow: 0 12px 30px rgba(0, 0, 0, 0.35);
    z-index: 20;
  }

  .menu-item {
    width: 100%;
    text-align: left;
    padding: 0.62rem 0.7rem;
    border: none;
    border-radius: 2px;
    background: transparent;
    color: rgba(var(--color-cream-rgb), 0.82);
    font-family: 'Cinzel', serif;
    font-size: 0.58rem;
    letter-spacing: 0.12em;
    text-transform: uppercase;
    cursor: pointer;
  }

  .menu-item:hover:not(:disabled) {
    background: rgba(var(--color-accent-rgb), 0.1);
  }

  .menu-item:disabled {
    opacity: 0.45;
    cursor: not-allowed;
  }

  .install-btn {
    flex: 1 1 0;
    width: auto;
    min-width: 0;
    padding: 0.86rem 0.96rem;
    font-family: 'Cinzel', serif;
    font-size: 0.66rem;
    font-weight: 600;
    letter-spacing: 0.2em;
    text-transform: uppercase;
    color: #1c0e03;
    background: linear-gradient(
      135deg,
      var(--color-gold-button) 0%,
      var(--color-gold-dark) 50%,
      var(--color-gold-button) 100%
    );
    background-size: 200% 100%;
    border: 1px solid rgba(210, 158, 60, 0.45);
    border-radius: 2px;
    box-shadow:
      0 0 0 1px rgba(var(--color-warm-bright-rgb), 0.14) inset,
      0 4px 18px rgba(170, 100, 25, 0.22);
    display: flex;
    align-items: center;
    justify-content: center;
    gap: 0.6rem;
    position: relative;
    overflow: hidden;
    cursor: pointer;
    transition: all 0.22s ease;
  }

  .install-btn::before {
    content: '';
    position: absolute;
    inset: 0;
    background: linear-gradient(
      180deg,
      rgba(255, 218, 128, 0.16) 0%,
      transparent 55%
    );
    pointer-events: none;
  }

  .install-btn:hover:not(:disabled) {
    background-position: 100% 0;
    box-shadow:
      0 0 0 1px rgba(var(--color-warm-bright-rgb), 0.2) inset,
      0 6px 30px rgba(170, 100, 25, 0.5),
      0 0 44px rgba(205, 150, 60, 0.15);
    transform: translateY(-1px);
  }

  .install-btn.install-btn-danger {
    color: #fff3ee;
    background: linear-gradient(135deg, #bf5442 0%, #842619 52%, #d46d5a 100%);
    border-color: rgba(226, 128, 110, 0.52);
    box-shadow:
      0 0 0 1px rgba(255, 181, 166, 0.16) inset,
      0 4px 22px rgba(132, 38, 25, 0.34);
  }

  .install-btn.install-btn-danger::before {
    background: linear-gradient(
      180deg,
      rgba(255, 216, 208, 0.14) 0%,
      transparent 55%
    );
  }

  .install-btn.install-btn-danger:hover:not(:disabled) {
    box-shadow:
      0 0 0 1px rgba(255, 181, 166, 0.22) inset,
      0 6px 30px rgba(132, 38, 25, 0.5),
      0 0 40px rgba(191, 84, 66, 0.18);
  }

  .install-btn:disabled {
    opacity: 0.32;
    cursor: not-allowed;
  }

  button:focus-visible {
    outline: 2px solid rgba(var(--color-warm-rgb), 0.9);
    outline-offset: 2px;
  }

  @media (max-width: 520px) {
    .action-primary {
      flex-direction: column;
    }

    .menu-trigger {
      width: 100%;
    }
  }
</style>
