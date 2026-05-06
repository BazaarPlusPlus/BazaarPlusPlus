<script lang="ts">
  import AppModal from '$lib/components/AppModal.svelte';
  import { locale } from '$lib/locale';
  import { describeRepairError, type RepairError } from '$lib/installer/repair-errors';

  export let open: boolean;
  export let acknowledged: boolean;
  export let confirming = false;
  export let body: string;
  export let error: RepairError | null = null;
  export let onConfirm: () => void | Promise<void>;
  export let onCancel: () => void | Promise<void>;

  function localized(zh: string, en: string): string {
    return $locale === 'zh' ? zh : en;
  }

  $: errorCopy = error ? describeRepairError(error, localized) : null;
  $: confirmText = errorCopy
    ? errorCopy.retryLabel
    : localized('确认重置', 'Confirm Reset');
</script>

<AppModal
  {open}
  eyebrow="BazaarPlusPlus"
  title={$locale === 'zh' ? '重置战绩记录' : 'Reset Match History'}
  {confirmText}
  cancelText={$locale === 'zh' ? '关闭' : 'Close'}
  confirmBusy={confirming}
  confirmBusyText={$locale === 'zh'
    ? '重置战绩记录中...'
    : 'Resetting match history...'}
  confirmDisabled={!acknowledged || confirming}
  showCancel={true}
  bodyClass="reset-history"
  wide={true}
  {onConfirm}
  {onCancel}
>
  <section class="reset-history-warning">
    <p class="reset-history-kicker">
      {$locale === 'zh' ? '危险操作' : 'Dangerous Action'}
    </p>
    <p class="reset-history-body">{body}</p>
  </section>

  {#if errorCopy}
    <section class="reset-history-error" role="alert" aria-live="polite">
      <p class="reset-history-error-title">{errorCopy.title}</p>
      <p class="reset-history-error-body">{errorCopy.body}</p>
      {#if errorCopy.paths && errorCopy.paths.length > 0}
        <p class="reset-history-error-list-label">{errorCopy.pathListLabel}</p>
        <ul class="reset-history-error-list">
          {#each errorCopy.paths as path (path)}
            <li class="reset-history-error-list-item" title={path}>{path}</li>
          {/each}
        </ul>
      {/if}
    </section>
  {/if}

  <label class="reset-history-acknowledge">
    <input
      class="reset-history-acknowledge-input"
      bind:checked={acknowledged}
      type="checkbox"
    />
    <span class="reset-history-acknowledge-box" aria-hidden="true"></span>
    <span>
      {$locale === 'zh'
        ? '我已知晓此操作会永久删除当前战绩记录，并确认继续'
        : 'I understand this will permanently delete the current match history, and I want to continue.'}
    </span>
  </label>
</AppModal>

<style>
  .reset-history-warning {
    display: grid;
    gap: 0.45rem;
    padding: 0.92rem 1rem;
    text-align: left;
    border: 1px solid rgba(191, 104, 81, 0.22);
    border-radius: 4px;
    background:
      linear-gradient(
        180deg,
        rgba(191, 104, 81, 0.08),
        rgba(191, 104, 81, 0.025)
      ),
      rgba(12, 8, 4, 0.78);
    box-shadow: inset 0 0 0 1px rgba(var(--color-warm-bright-rgb), 0.03);
  }

  .reset-history-kicker {
    margin: 0;
    font-family: 'Cinzel', serif;
    font-size: 0.6rem;
    letter-spacing: 0.18em;
    text-transform: uppercase;
    color: rgba(240, 178, 162, 0.88);
  }

  .reset-history-body {
    margin: 0;
    font-size: 0.84rem;
    line-height: 1.65;
    color: rgba(var(--color-cream-rgb), 0.82);
    white-space: pre-line;
  }

  .reset-history-error {
    display: grid;
    gap: 0.5rem;
    padding: 0.92rem 1rem;
    border: 1px solid rgba(220, 90, 70, 0.42);
    border-radius: 4px;
    background: linear-gradient(
        180deg,
        rgba(220, 90, 70, 0.16),
        rgba(220, 90, 70, 0.05)
      ),
      rgba(20, 8, 4, 0.85);
    box-shadow: 0 0 0 1px rgba(255, 181, 166, 0.08) inset;
  }

  .reset-history-error-title {
    margin: 0;
    font-family: 'Cinzel', serif;
    font-size: 0.7rem;
    letter-spacing: 0.18em;
    text-transform: uppercase;
    color: rgba(255, 200, 188, 0.95);
  }

  .reset-history-error-body {
    margin: 0;
    font-size: 0.82rem;
    line-height: 1.6;
    color: rgba(var(--color-cream-rgb), 0.86);
  }

  .reset-history-error-list-label {
    margin: 0.2rem 0 0;
    font-size: 0.7rem;
    letter-spacing: 0.08em;
    text-transform: uppercase;
    color: rgba(240, 178, 162, 0.78);
  }

  .reset-history-error-list {
    margin: 0;
    padding: 0;
    list-style: none;
    display: grid;
    gap: 0.2rem;
    max-height: 8rem;
    overflow-y: auto;
  }

  .reset-history-error-list-item {
    font-family: 'Fira Code', monospace;
    font-size: 0.7rem;
    color: rgba(225, 210, 185, 0.88);
    word-break: break-all;
  }

  .reset-history-acknowledge {
    display: grid;
    grid-template-columns: auto auto 1fr;
    gap: 0.7rem;
    align-items: start;
    padding: 0.8rem 0.88rem;
    border: 1px solid rgba(191, 104, 81, 0.24);
    border-radius: 4px;
    background:
      linear-gradient(
        180deg,
        rgba(191, 104, 81, 0.08),
        rgba(191, 104, 81, 0.025)
      ),
      rgba(12, 8, 4, 0.78);
    box-shadow: inset 0 0 0 1px rgba(var(--color-warm-bright-rgb), 0.04);
    text-align: left;
    color: rgba(var(--color-cream-rgb), 0.8);
    font-size: 0.8rem;
    line-height: 1.45;
    cursor: pointer;
  }

  .reset-history-acknowledge-input {
    position: absolute;
    opacity: 0;
    pointer-events: none;
  }

  .reset-history-acknowledge-box {
    width: 1.15rem;
    height: 1.15rem;
    margin-top: 0.08rem;
    border: 1px solid rgba(244, 227, 188, 0.58);
    border-radius: 0.28rem;
    background: linear-gradient(
      180deg,
      rgba(255, 255, 255, 0.09),
      rgba(255, 255, 255, 0.03)
    );
    box-shadow:
      0 0 0 1px rgba(var(--color-warm-bright-rgb), 0.05) inset,
      0 2px 10px rgba(0, 0, 0, 0.16);
    position: relative;
    transition:
      border-color 0.15s ease,
      background 0.15s ease,
      box-shadow 0.15s ease,
      transform 0.15s ease;
  }

  .reset-history-acknowledge-box::after {
    content: '';
    position: absolute;
    left: 0.33rem;
    top: 0.14rem;
    width: 0.32rem;
    height: 0.62rem;
    border-right: 2px solid transparent;
    border-bottom: 2px solid transparent;
    transform: rotate(45deg);
    transition: border-color 0.15s ease;
  }

  .reset-history-acknowledge-input:checked + .reset-history-acknowledge-box {
    border-color: rgba(240, 201, 120, 0.62);
    background: linear-gradient(
      180deg,
      rgba(212, 160, 64, 0.28),
      rgba(158, 92, 30, 0.22)
    );
    box-shadow:
      0 0 0 1px rgba(var(--color-warm-bright-rgb), 0.12) inset,
      0 4px 14px rgba(170, 100, 25, 0.24);
  }

  .reset-history-acknowledge-input:checked
    + .reset-history-acknowledge-box::after {
    border-color: #fff2ca;
  }

  .reset-history-acknowledge:hover .reset-history-acknowledge-box {
    border-color: rgba(var(--color-warm-rgb), 0.8);
    transform: translateY(-1px);
  }

  .reset-history-acknowledge-input:focus-visible
    + .reset-history-acknowledge-box {
    outline: 2px solid rgba(var(--color-warm-rgb), 0.9);
    outline-offset: 2px;
  }

  @media (max-width: 520px) {
    .reset-history-warning,
    .reset-history-acknowledge {
      padding-left: 0.85rem;
      padding-right: 0.85rem;
    }
  }
</style>
