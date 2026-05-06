<script lang="ts">
  import InstallerStatusSteps from '$lib/components/installer/InstallerStatusSteps.svelte';
  import StreamModePanel from '$lib/components/stream/StreamModePanel.svelte';
  import StreamRecordLibrary from '$lib/components/stream/StreamRecordLibrary.svelte';
  import type { EnvironmentInfo } from '$lib/types';
  import type { ActionBusy, StepState } from '$lib/installer/state';
  import type { TranslateText } from '$lib/installer/selectors/types.ts';

  export let showStreamMode: boolean;
  export let locale: string;
  export let effectiveGamePath: string;
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
  export let t: TranslateText;
  export let onPickGamePath: () => void | Promise<void>;
  export let onCheckPath: () => void | Promise<void>;
  export let onRequestInstall: () => void | Promise<void>;
  export let onRepair: () => void | Promise<void>;
  export let onUninstall: () => void | Promise<void>;
  export let onLaunchGame: () => void | Promise<void>;
  export let onResetBazaar: () => void;
  export let onCustomGamePathInput: () => void;
</script>

{#if showStreamMode}
  <section class="embedded-stream-shell">
    <StreamModePanel
      gamePath={effectiveGamePath || null}
      eyebrow={locale === 'zh' ? '直播模式' : 'Stream Mode'}
      title={locale === 'zh' ? '直播模式' : 'Stream Mode'}
    />
  </section>
  <StreamRecordLibrary gamePath={effectiveGamePath || null} />
{:else}
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
    effectiveGamePath={effectiveGamePath}
    {t}
    onPickGamePath={onPickGamePath}
    onCheckPath={onCheckPath}
    onRequestInstall={onRequestInstall}
    onRepair={onRepair}
    onUninstall={onUninstall}
    onLaunchGame={onLaunchGame}
    onResetBazaar={onResetBazaar}
    onCustomGamePathInput={onCustomGamePathInput}
  />
{/if}

<style>
  .embedded-stream-shell {
    padding: 0.95rem 1.05rem;
    background:
      radial-gradient(
        circle at top left,
        rgba(var(--color-warm-rgb), 0.08),
        transparent 42%
      ),
      linear-gradient(180deg, rgba(20, 12, 6, 0.96), rgba(12, 7, 4, 0.94));
    border: 1px solid rgba(var(--color-accent-rgb), 0.15);
    border-radius: 3px;
    box-shadow:
      0 8px 28px rgba(0, 0, 0, 0.3),
      inset 0 0 0 1px rgba(var(--color-warm-rgb), 0.04);
  }
</style>
