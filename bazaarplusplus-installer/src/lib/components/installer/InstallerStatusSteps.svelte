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
  export let bundledBppVersion: string | null;
  export let installedBppVersion: string | null;
  export let bazaarFound: boolean;
  export let bazaarChecking: boolean;
  export let bazaarInvalid: boolean;
  export let customGamePath: string;
  export let hasPath: boolean;
  export let isBusy: boolean;
  export let actionBusy: ActionBusy;
  export let canInstall: boolean;
  export let canLaunchGame: boolean;
  export let dotnetDownloadUrl: string;
  export let effectiveGamePath: string;
  export let t: (
    key: keyof typeof messages.en,
    params?: Record<string, string | number>
  ) => string;
  export let onPickGamePath: () => void | Promise<void>;
  export let onCheckPath: () => void | Promise<void>;
  export let onRequestInstall: () => void | Promise<void>;
  export let onRepair: () => void | Promise<void>;
  export let onUninstall: () => void | Promise<void>;
  export let onLaunchGame: () => void | Promise<void>;
  export let onResetBazaar: () => void | Promise<void>;
  export let onCustomGamePathInput: () => void;

  let actionMenuOpen = false;

  function toggleActionMenu() {
    actionMenuOpen = !actionMenuOpen;
  }

  async function handleUninstall() {
    actionMenuOpen = false;
    await onUninstall();
  }
</script>

<div class="steps">
  <div
    class="step"
    class:step-found={modInstalled && !versionMismatch}
    class:step-error={versionMismatch}
  >
    <div class="step-index" aria-hidden="true">I</div>
    <div class="step-body step-body-bpp">
      <div class="step-bpp-content">
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
        {#if versionMismatch}
          <div class="mismatch-summary">
            <p class="detail-line detail-muted">
              {$locale === 'zh'
                ? '已安装版本和安装器版本不同，建议重新安装'
                : 'The installed version differs from the bundled one. Check what changed before reinstalling.'}
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
      <div class="step-bpp-action">
        <a class="secondary-btn mismatch-link-button" href="/whats-new">
          {$locale === 'zh' ? "What's New" : "What's New"}
        </a>
      </div>
    </div>
  </div>

  <div
    class="step"
    class:step-found={bazaarFound && Boolean(effectiveGamePath)}
  >
    <div class="step-index" aria-hidden="true">II</div>
    <div class="step-body">
      <span class="step-title">
        {t('stepBazaar')}
        {#if bazaarFound && effectiveGamePath}
          <span class="tag tag-ok">{t('statusFound')}</span>
        {/if}
      </span>

      {#if bazaarFound && effectiveGamePath}
        <p class="detail-line detail-path" title={effectiveGamePath}>
          {effectiveGamePath}
        </p>
        <button class="redetect-btn" onclick={onResetBazaar} type="button"
          >{t('actionReenter')}</button
        >
      {:else}
        <div class="locate-bar" class:locate-bar-invalid={bazaarInvalid}>
          <button
            class="locate-browse"
            onclick={onPickGamePath}
            type="button"
            disabled={bazaarChecking}
          >
            {t('actionBrowse')}
          </button>
          <div class="locate-input-wrap">
            <input
              bind:value={customGamePath}
              class="path-input"
              placeholder={t('placeholderGamePath')}
              type="text"
              spellcheck="false"
              onkeydown={(e) => e.key === 'Enter' && onCheckPath()}
              oninput={onCustomGamePathInput}
            />
          </div>
          <button
            class="locate-confirm"
            onclick={onCheckPath}
            type="button"
            disabled={!hasPath || bazaarChecking}
          >
            {#if bazaarChecking}
              <span class="spinner" aria-hidden="true"></span>
            {:else}
              {t('actionCheck')}
            {/if}
          </button>
        </div>
        {#if bazaarInvalid}
          <p class="locate-error">{t('errorGamePath')}</p>
        {/if}
      {/if}
    </div>
  </div>

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
</div>

<style>
  .steps {
    display: grid;
    gap: 0.5rem;
  }

  .step {
    display: flex;
    gap: 1rem;
    align-items: flex-start;
    padding: 0.95rem 1.05rem;
    background: rgba(18, 11, 5, 0.88);
    border: 1px solid rgba(180, 130, 48, 0.13);
    border-radius: 3px;
    box-shadow: 0 6px 28px rgba(0, 0, 0, 0.35);
    transition:
      border-color 0.3s ease,
      box-shadow 0.3s ease;
  }

  .step-found {
    border-color: rgba(90, 200, 130, 0.25);
    box-shadow:
      0 6px 28px rgba(0, 0, 0, 0.35),
      0 0 18px rgba(90, 200, 130, 0.05);
  }

  .step-error {
    border-color: rgba(196, 98, 76, 0.28);
    box-shadow:
      0 6px 28px rgba(0, 0, 0, 0.35),
      0 0 14px rgba(196, 98, 76, 0.04);
  }

  .step-install {
    margin-top: 0.2rem;
  }

  .step-index {
    font-family: 'Cinzel', serif;
    font-size: 0.55rem;
    letter-spacing: 0.15em;
    color: rgba(200, 148, 55, 0.4);
    padding-top: 0.25rem;
    flex-shrink: 0;
    width: 1.4rem;
    text-align: center;
  }

  .step-body {
    flex: 1;
    display: grid;
    gap: 0.7rem;
    min-width: 0;
    overflow: visible;
  }

  .step-body-bpp {
    display: grid;
    grid-template-columns: minmax(0, 1fr) 8.8rem;
    align-items: stretch;
    gap: 1.1rem;
  }

  .step-bpp-content {
    display: grid;
    gap: 0.7rem;
    min-width: 0;
  }

  .step-bpp-action {
    display: flex;
    align-items: flex-end;
    justify-content: center;
    min-width: 0;
  }

  .step-title {
    font-family: 'Cinzel', serif;
    font-size: 0.72rem;
    letter-spacing: 0.12em;
    text-transform: uppercase;
    color: rgba(220, 195, 145, 0.8);
    display: flex;
    align-items: center;
    gap: 0.65rem;
    min-width: 0;
  }

  .step-heading {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 0.75rem;
    flex-wrap: wrap;
  }

  .tag {
    font-family: 'Fira Code', monospace;
    font-size: 0.65rem;
    letter-spacing: 0;
    text-transform: none;
    padding: 0.18rem 0.55rem;
    border-radius: 2px;
    flex: 1;
    min-width: 0;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .tag-ok {
    background: rgba(80, 180, 120, 0.15);
    color: #6dd9a0;
    border: 1px solid rgba(80, 180, 120, 0.25);
  }
  .tag-warn {
    background: rgba(200, 140, 50, 0.12);
    color: #c4923a;
    border: 1px solid rgba(200, 140, 50, 0.22);
  }
  .tag-danger {
    background: rgba(191, 104, 81, 0.1);
    color: #f0b2a2;
    border: 1px solid rgba(191, 104, 81, 0.2);
  }

  .runtime-chip {
    flex: 0 0 auto;
    min-width: 0;
    padding: 0.32rem 0.58rem;
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
    outline: 2px solid rgba(255, 214, 140, 0.9);
    outline-offset: 2px;
  }

  .runtime-chip-label {
    font-family: 'Cinzel', serif;
    font-size: 0.5rem;
    letter-spacing: 0.12em;
    text-transform: uppercase;
    color: rgba(196, 168, 120, 0.72);
  }

  .runtime-chip-value {
    font-family: 'Fira Code', monospace;
    font-size: 0.62rem;
    letter-spacing: 0;
    text-transform: none;
    color: currentColor;
  }

  .detail-line {
    margin: 0;
    min-width: 0;
  }

  .detail-path {
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    user-select: text;
    font-family: 'Fira Code', monospace;
    font-size: 0.73rem;
    color: rgba(228, 216, 191, 0.82);
  }

  .detail-muted {
    font-size: 0.8rem;
    color: rgba(200, 170, 120, 0.6);
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

  .mismatch-link-button {
    text-decoration: none;
    width: 100%;
    min-height: 3.4rem;
    padding-left: 0.8rem;
    padding-right: 0.8rem;
    line-height: 1.5;
    text-align: center;
  }

  .mismatch-version {
    display: inline-flex;
    align-items: center;
    gap: 0.45rem;
    min-width: 0;
    padding: 0.32rem 0.52rem;
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
    font-size: 0.68rem;
    white-space: nowrap;
  }

  .mismatch-version-ok .mismatch-version-label {
    color: rgba(156, 214, 179, 0.78);
  }

  .mismatch-version-value {
    color: rgba(235, 223, 198, 0.86);
    font-family: 'Fira Code', monospace;
    font-size: 0.72rem;
    white-space: nowrap;
  }

  .mismatch-version-ok .mismatch-version-value {
    color: rgba(216, 244, 228, 0.92);
  }

  .locate-bar {
    display: flex;
    align-items: stretch;
    border: 1px solid rgba(180, 130, 48, 0.2);
    border-radius: 2px;
    overflow: hidden;
    background: rgba(6, 4, 2, 0.85);
    transition: border-color 0.18s ease;
  }

  .locate-bar:focus-within {
    border-color: rgba(200, 148, 55, 0.4);
  }

  .locate-browse {
    flex-shrink: 0;
    padding: 0.68rem 0.9rem;
    font-family: 'Cinzel', serif;
    font-size: 0.58rem;
    letter-spacing: 0.16em;
    text-transform: uppercase;
    color: rgba(200, 155, 72, 0.7);
    background: rgba(200, 148, 55, 0.06);
    border: none;
    border-right: 1px solid rgba(180, 130, 48, 0.18);
    cursor: pointer;
    transition:
      background 0.15s ease,
      color 0.15s ease;
    white-space: nowrap;
  }

  .locate-browse:hover {
    background: rgba(200, 148, 55, 0.12);
    color: rgba(220, 180, 100, 0.9);
  }

  .locate-input-wrap {
    flex: 1;
    min-width: 0;
    display: flex;
    align-items: center;
    padding: 0 0.75rem;
  }

  .path-input {
    width: 100%;
    background: none;
    border: none;
    color: rgba(225, 210, 185, 0.88);
    font-family: 'Fira Code', monospace;
    font-size: 0.78rem;
    min-width: 0;
    user-select: text;
  }

  .path-input::placeholder {
    color: rgba(150, 120, 75, 0.35);
    font-style: italic;
  }

  .locate-confirm {
    flex-shrink: 0;
    padding: 0.68rem 0.9rem;
    font-family: 'Cinzel', serif;
    font-size: 0.58rem;
    letter-spacing: 0.16em;
    text-transform: uppercase;
    color: rgba(200, 155, 72, 0.7);
    background: rgba(200, 148, 55, 0.06);
    border: none;
    border-left: 1px solid rgba(180, 130, 48, 0.18);
    cursor: pointer;
    transition:
      background 0.15s ease,
      color 0.15s ease;
    white-space: nowrap;
  }

  .locate-confirm:hover:not(:disabled) {
    background: rgba(200, 148, 55, 0.14);
    color: rgba(220, 180, 100, 0.9);
  }

  .locate-confirm:disabled {
    opacity: 0.3;
    cursor: not-allowed;
  }

  .locate-bar-invalid {
    border-color: rgba(200, 80, 60, 0.45) !important;
  }

  .locate-error {
    margin: 0;
    font-family: 'Fira Code', monospace;
    font-size: 0.72rem;
    color: rgba(220, 100, 80, 0.8);
    animation: fade-up 0.2s ease both;
  }

  button {
    cursor: pointer;
    border: none;
    font: inherit;
  }

  button:focus-visible,
  .path-input:focus-visible {
    outline: 2px solid rgba(255, 214, 140, 0.9);
    outline-offset: 2px;
  }

  .secondary-btn {
    padding: 0.68rem 0.9rem;
    font-family: 'Cinzel', serif;
    font-size: 0.58rem;
    letter-spacing: 0.16em;
    text-transform: uppercase;
    color: rgba(200, 155, 72, 0.78);
    background: rgba(200, 148, 55, 0.06);
    border: 1px solid rgba(180, 130, 48, 0.18);
    border-radius: 2px;
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
    background: rgba(200, 148, 55, 0.12);
    color: rgba(220, 180, 100, 0.95);
    border-color: rgba(200, 148, 55, 0.34);
  }

  .secondary-btn:disabled {
    opacity: 0.35;
    cursor: not-allowed;
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
    border-radius: 2px;
    background: transparent;
    color: rgba(228, 216, 191, 0.82);
    font-family: 'Cinzel', serif;
    font-size: 0.58rem;
    letter-spacing: 0.12em;
    text-transform: uppercase;
  }

  .menu-item:hover:not(:disabled) {
    background: rgba(200, 148, 55, 0.1);
  }

  .menu-item:disabled {
    opacity: 0.45;
    cursor: not-allowed;
  }

  .redetect-btn {
    align-self: start;
    padding: 0.38rem 0.8rem;
    font-family: 'Cinzel', serif;
    font-size: 0.54rem;
    letter-spacing: 0.18em;
    text-transform: uppercase;
    color: rgba(160, 120, 55, 0.55);
    border: 1px solid rgba(160, 120, 55, 0.18);
    border-radius: 2px;
    background: none;
    transition: all 0.15s ease;
  }

  .redetect-btn:hover {
    color: rgba(200, 160, 80, 0.8);
    border-color: rgba(200, 148, 55, 0.35);
  }

  .install-btn {
    flex: 1 1 0;
    width: auto;
    min-width: 0;
    padding: 0.95rem 1rem;
    font-family: 'Cinzel', serif;
    font-size: 0.72rem;
    font-weight: 600;
    letter-spacing: 0.24em;
    text-transform: uppercase;
    color: #1c0e03;
    background: linear-gradient(135deg, #d4a040 0%, #9e5c1e 50%, #d4a040 100%);
    background-size: 200% 100%;
    border: 1px solid rgba(210, 158, 60, 0.45);
    border-radius: 2px;
    box-shadow:
      0 0 0 1px rgba(255, 198, 98, 0.14) inset,
      0 4px 22px rgba(170, 100, 25, 0.3);
    display: flex;
    align-items: center;
    justify-content: center;
    gap: 0.6rem;
    position: relative;
    overflow: hidden;
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
      0 0 0 1px rgba(255, 198, 98, 0.2) inset,
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

  .spinner {
    display: inline-block;
    width: 11px;
    height: 11px;
    border: 1.5px solid rgba(200, 165, 100, 0.25);
    border-top-color: rgba(200, 165, 100, 0.75);
    border-radius: 50%;
    animation: spin 0.75s linear infinite;
    flex-shrink: 0;
  }

  .spinner.dark {
    border-color: rgba(30, 15, 4, 0.25);
    border-top-color: rgba(30, 15, 4, 0.7);
  }

  @keyframes spin {
    to {
      transform: rotate(360deg);
    }
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
    .step-heading {
      align-items: flex-start;
    }

    .action-primary {
      flex-direction: column;
    }

    .step-body-bpp {
      grid-template-columns: 1fr;
      gap: 0.7rem;
    }

    .step-bpp-action {
      justify-content: stretch;
    }

    .menu-trigger {
      width: 100%;
    }
  }
</style>
