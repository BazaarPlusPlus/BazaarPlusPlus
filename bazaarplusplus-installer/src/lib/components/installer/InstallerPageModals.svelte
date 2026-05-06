<script lang="ts">
  import AppModal from '$lib/components/AppModal.svelte';
  import InstallerInstallPreviewModal from '$lib/components/installer/InstallerInstallPreviewModal.svelte';
  import InstallerResetHistoryModal from '$lib/components/installer/InstallerResetHistoryModal.svelte';
  import type { RepairError } from '$lib/installer/repair-errors';
  import type { TranslateText } from '$lib/installer/selectors/types.ts';

  export let showInstallModal: boolean;
  export let bilibiliUrl: string;
  export let installAcknowledged: boolean;
  export let installConfirmationBusy: boolean;
  export let showRepairModal: boolean;
  export let repairModalBody: string;
  export let repairAcknowledged: boolean;
  export let repairConfirming: boolean;
  export let repairError: RepairError | null = null;
  export let showLaunchOptionsWarningModal: boolean;
  export let showSteamQuitModal: boolean;
  export let steamModalTitle: string;
  export let steamModalBody: string;
  export let steamModalCancelText: string;
  export let steamActionBusy: boolean;
  export let showUpdaterReviewModal: boolean;
  export let updaterReviewBody: string;
  export let updaterReviewBusy: boolean;
  export let showUpdaterModal: boolean;
  export let updaterModalTitle: string;
  export let updaterModalBody: string;
  export let t: TranslateText;
  export let onOpenBilibili: (event?: MouseEvent) => void | Promise<void>;
  export let onConfirmInstall: () => void | Promise<void>;
  export let onConfirmRepair: () => void | Promise<void>;
  export let onCancelRepair: () => void;
  export let onCloseLaunchOptionsWarning: () => void;
  export let onConfirmSteamQuit: () => void | Promise<void>;
  export let onCancelSteamQuit: () => void | Promise<void>;
  export let onConfirmUpdaterReview: () => void | Promise<void>;
  export let onCancelUpdaterReview: () => void;
  export let onCloseUpdaterModal: () => void;
</script>

<InstallerInstallPreviewModal
  open={showInstallModal}
  bind:installAcknowledged
  confirming={installConfirmationBusy}
  {bilibiliUrl}
  {onOpenBilibili}
  onConfirm={onConfirmInstall}
/>

<InstallerResetHistoryModal
  open={showRepairModal}
  body={repairModalBody}
  bind:acknowledged={repairAcknowledged}
  confirming={repairConfirming}
  error={repairError}
  onConfirm={onConfirmRepair}
  onCancel={onCancelRepair}
/>

<AppModal
  open={showLaunchOptionsWarningModal}
  eyebrow="BazaarPlusPlus"
  title={t('launchOptionsWarningTitle')}
  body={t('launchOptionsWarningBody')}
  confirmText={t('actionClose')}
  onConfirm={onCloseLaunchOptionsWarning}
/>

<AppModal
  open={showSteamQuitModal}
  eyebrow="BazaarPlusPlus"
  title={steamModalTitle}
  body={steamModalBody}
  confirmText={t('actionQuitSteam')}
  cancelText={steamModalCancelText}
  showCancel={true}
  confirmBusy={steamActionBusy}
  confirmBusyText={t('actionQuitSteam')}
  onConfirm={onConfirmSteamQuit}
  onCancel={onCancelSteamQuit}
/>

<AppModal
  open={showUpdaterReviewModal}
  eyebrow="BazaarPlusPlus"
  title={t('updaterReviewTitle')}
  body={updaterReviewBody}
  confirmText={t('updaterReviewConfirm')}
  cancelText={t('updaterReviewCancel')}
  showCancel={true}
  confirmBusy={updaterReviewBusy}
  confirmBusyText={t('updaterInstalling')}
  onConfirm={onConfirmUpdaterReview}
  onCancel={onCancelUpdaterReview}
/>

<AppModal
  open={showUpdaterModal}
  eyebrow="BazaarPlusPlus"
  title={updaterModalTitle}
  body={updaterModalBody}
  confirmText={t('actionClose')}
  onConfirm={onCloseUpdaterModal}
/>
