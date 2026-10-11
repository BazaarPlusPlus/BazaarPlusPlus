import { FolderX, ShieldCheck } from 'lucide-react';
import {
  ConfirmDialog,
  ConfirmNote,
  ConfirmTarget
} from '../../components/ui/ConfirmDialog';
import { useI18n } from '../../i18n/LocaleProvider';
import { InstallProblemBanner } from './InstallProblemBanner';
import { ResetDataFailureDetails } from './ResetDataFailureDetails';
import type { InstallProblem } from './installProblems';

export function LegacyRootDeleteConfirmModal({
  busy,
  acknowledged,
  targetPath,
  name,
  problem,
  failurePaths,
  onAcknowledgedChange,
  onClose,
  onConfirm
}: {
  busy: boolean;
  acknowledged: boolean;
  targetPath: string;
  name: string;
  problem: InstallProblem | null;
  failurePaths: string[];
  onAcknowledgedChange: (acknowledged: boolean) => void;
  onClose: () => void;
  onConfirm: () => void | Promise<void>;
}) {
  const { t } = useI18n();

  return (
    <ConfirmDialog
      titleId="legacy-delete-modal-title"
      title={t('legacyDeleteConfirmTitle')}
      tone="danger"
      acknowledge={{
        label: t('legacyDeleteConfirmAcknowledge', { name }),
        checked: acknowledged,
        onChange: onAcknowledgedChange
      }}
      confirmLabel={problem ? t('retry') : t('legacyDeleteConfirmAction')}
      busyLabel={t('legacyDeleteRunning')}
      busy={busy}
      activeDismissalPolicy={{ kind: 'blocked' }}
      dismissLabel={problem ? t('close') : undefined}
      onConfirm={onConfirm}
      onClose={onClose}
    >
      <ConfirmTarget>
        {t('legacyDeleteTarget', { path: targetPath, name })}
      </ConfirmTarget>
      <ConfirmNote tone="danger" icon={<FolderX size={15} />}>
        <p>{t('legacyDeleteConfirmBody', { name })}</p>
        <p>{t('legacyDeleteConfirmSpace')}</p>
      </ConfirmNote>
      <ConfirmNote tone="neutral" icon={<ShieldCheck size={15} />}>
        <p>{t('legacyDeleteConfirmKeepsCurrent')}</p>
        <p>{t('resetDataConfirmGameClosed')}</p>
      </ConfirmNote>
      {problem && <InstallProblemBanner problem={problem} />}
      {failurePaths.length > 0 && (
        <ResetDataFailureDetails paths={failurePaths} />
      )}
    </ConfirmDialog>
  );
}
