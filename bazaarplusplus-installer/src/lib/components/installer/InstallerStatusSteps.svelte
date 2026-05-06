<script lang="ts">
  import { messages } from '$lib/i18n';
  import type { EnvironmentInfo } from '$lib/types';
  import type { ActionBusy, StepState } from '$lib/installer/state';
  import InstallerBppStep from './InstallerBppStep.svelte';
  import InstallerBazaarStep from './InstallerBazaarStep.svelte';
  import InstallerActionsStep from './InstallerActionsStep.svelte';

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
</script>

<div class="steps">
  <InstallerBppStep
    {env}
    {modInstalled}
    {versionMismatch}
    {bundledBppVersion}
    {installedBppVersion}
    {actionBusy}
    {t}
  />

  <InstallerBazaarStep
    {bazaarFound}
    {bazaarChecking}
    {bazaarInvalid}
    bind:customGamePath
    {hasPath}
    {effectiveGamePath}
    {t}
    {onPickGamePath}
    {onCheckPath}
    {onResetBazaar}
    {onCustomGamePathInput}
  />

  <InstallerActionsStep
    {env}
    {dotnetState}
    {modInstalled}
    {versionMismatch}
    {isBusy}
    {actionBusy}
    {canInstall}
    {canLaunchGame}
    {dotnetDownloadUrl}
    {t}
    {onRequestInstall}
    {onRepair}
    {onUninstall}
    {onLaunchGame}
  />
</div>

<style>
  .steps {
    display: grid;
    gap: 0.5rem;
  }

  /* Shared step shell + tag/detail/spinner styles. Kept :global so the
     three substep components below render against the same visual contract
     without duplicating CSS in each file. */

  :global(.step) {
    display: flex;
    gap: 0.85rem;
    align-items: flex-start;
    padding: 0.82rem 0.92rem;
    background: rgba(18, 11, 5, 0.76);
    border: 1px solid rgba(180, 130, 48, 0.11);
    border-radius: 3px;
    box-shadow: 0 6px 18px rgba(0, 0, 0, 0.22);
    transition:
      border-color 0.3s ease,
      box-shadow 0.3s ease;
  }

  :global(.step-found) {
    border-color: rgba(90, 200, 130, 0.25);
    box-shadow:
      0 6px 18px rgba(0, 0, 0, 0.22),
      0 0 18px rgba(90, 200, 130, 0.05);
  }

  :global(.step-error) {
    border-color: rgba(196, 98, 76, 0.28);
    box-shadow:
      0 6px 18px rgba(0, 0, 0, 0.22),
      0 0 14px rgba(196, 98, 76, 0.04);
  }

  :global(.step-install) {
    margin-top: 0.2rem;
  }

  :global(.step-index) {
    font-family: 'Cinzel', serif;
    font-size: 0.5rem;
    letter-spacing: 0.15em;
    color: rgba(var(--color-accent-rgb), 0.34);
    padding-top: 0.2rem;
    flex-shrink: 0;
    width: 1.2rem;
    text-align: center;
  }

  :global(.step-body) {
    flex: 1;
    display: grid;
    gap: 0.58rem;
    min-width: 0;
    overflow: visible;
  }

  :global(.step-title) {
    font-family: 'Cinzel', serif;
    font-size: 0.66rem;
    letter-spacing: 0.1em;
    text-transform: uppercase;
    color: rgba(220, 195, 145, 0.74);
    display: flex;
    align-items: center;
    gap: 0.55rem;
    min-width: 0;
  }

  :global(.step-heading) {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 0.75rem;
    flex-wrap: wrap;
  }

  :global(.tag) {
    font-family: 'Fira Code', monospace;
    font-size: 0.6rem;
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

  :global(.tag-ok) {
    background: rgba(80, 180, 120, 0.15);
    color: #6dd9a0;
    border: 1px solid rgba(80, 180, 120, 0.25);
  }

  :global(.tag-warn) {
    background: rgba(200, 140, 50, 0.12);
    color: #c4923a;
    border: 1px solid rgba(200, 140, 50, 0.22);
  }

  :global(.tag-danger) {
    background: rgba(191, 104, 81, 0.1);
    color: #f0b2a2;
    border: 1px solid rgba(191, 104, 81, 0.2);
  }

  :global(.detail-line) {
    margin: 0;
    min-width: 0;
  }

  :global(.detail-path) {
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    user-select: text;
    font-family: 'Fira Code', monospace;
    font-size: 0.7rem;
    color: rgba(var(--color-cream-rgb), 0.82);
  }

  :global(.detail-muted) {
    font-size: 0.76rem;
    color: rgba(var(--color-muted-gold-rgb), 0.58);
  }

  :global(.spinner) {
    display: inline-block;
    width: 11px;
    height: 11px;
    border: 1.5px solid rgba(200, 165, 100, 0.25);
    border-top-color: rgba(200, 165, 100, 0.75);
    border-radius: 50%;
    animation: spin 0.75s linear infinite;
    flex-shrink: 0;
  }

  :global(.spinner.dark) {
    border-color: rgba(30, 15, 4, 0.25);
    border-top-color: rgba(30, 15, 4, 0.7);
  }

  @keyframes -global-spin {
    to {
      transform: rotate(360deg);
    }
  }

  @media (max-width: 520px) {
    :global(.step-heading) {
      align-items: flex-start;
    }
  }
</style>
