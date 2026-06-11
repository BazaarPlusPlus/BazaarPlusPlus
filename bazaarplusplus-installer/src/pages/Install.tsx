import { useState } from 'react';
import { PageShell } from '../components/ui/PageShell';
import { InstallActionsPanel } from '../features/install/InstallActionsPanel';
import { InstallConfirmModal } from '../features/install/InstallConfirmModal';
import { InstallStatusPanel } from '../features/install/InstallStatusPanel';
import { ResetDataConfirmModal } from '../features/install/ResetDataConfirmModal';
import { useInstallPage } from '../features/install/useInstallPage';
import { useI18n } from '../i18n/LocaleProvider';

export default function Install() {
  const { t } = useI18n();
  const page = useInstallPage();
  const [showInstallModal, setShowInstallModal] = useState(false);
  const [showResetDataModal, setShowResetDataModal] = useState(false);
  const [installAcknowledged, setInstallAcknowledged] = useState(false);
  const [resetDataAcknowledged, setResetDataAcknowledged] = useState(false);
  const [compatOptIn, setCompatOptIn] = useState(false);
  const primaryMode: 'install' | 'reinstall' | 'launch' = !page.state.mod_state
    .installed
    ? 'install'
    : page.state.mod_state.version_matches
      ? 'launch'
      : 'reinstall';

  const openInstallModal = () => {
    setShowInstallModal(true);
    setInstallAcknowledged(false);
    // Seed the checkbox from the current desired mode: forced (checked + locked) on
    // macOS 27+, the persisted choice on <= 26, off elsewhere.
    setCompatOptIn(page.state.compat.desired);
  };

  const confirmInstall = async () => {
    const installed = await page.install(compatOptIn);
    if (installed) {
      setShowInstallModal(false);
      setInstallAcknowledged(false);
    }
  };

  const openResetDataModal = () => {
    setShowResetDataModal(true);
    setResetDataAcknowledged(false);
  };

  const confirmResetData = async () => {
    await page.resetData();
    setShowResetDataModal(false);
    setResetDataAcknowledged(false);
  };

  return (
    <PageShell eyebrow="Install" title={t('installTitle')}>
      <div className="grid grid-cols-12 gap-8 w-full">
        <InstallStatusPanel page={page} />
        <InstallActionsPanel
          page={page}
          primaryMode={primaryMode}
          onOpenInstallModal={openInstallModal}
          onOpenResetDataModal={openResetDataModal}
        />
      </div>

      {showInstallModal && (
        <InstallConfirmModal
          page={page}
          installAcknowledged={installAcknowledged}
          onAcknowledgedChange={setInstallAcknowledged}
          compat={page.state.compat}
          compatOptIn={compatOptIn}
          onCompatOptInChange={setCompatOptIn}
          onClose={() => setShowInstallModal(false)}
          onConfirm={confirmInstall}
        />
      )}

      {showResetDataModal && (
        <ResetDataConfirmModal
          page={page}
          acknowledged={resetDataAcknowledged}
          onAcknowledgedChange={setResetDataAcknowledged}
          onClose={() => setShowResetDataModal(false)}
          onConfirm={confirmResetData}
        />
      )}
    </PageShell>
  );
}
