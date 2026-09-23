import { Database, ShieldCheck } from 'lucide-react';
import {
  ConfirmDialog,
  ConfirmNote,
  ConfirmTarget
} from '../../components/ui/ConfirmDialog';
import { useI18n } from '../../i18n/LocaleProvider';
import { InstallProblemBanner } from './InstallProblemBanner';
import { ResetDataFailureDetails } from './ResetDataFailureDetails';
import type { InstallProblem } from './installProblems';

export function ResetDataConfirmModal({
  busy,
  acknowledged,
  targetPath,
  problem,
  failurePaths,
  onAcknowledgedChange,
  onClose,
  onConfirm
}: {
  busy: boolean;
  acknowledged: boolean;
  targetPath: string;
  problem: InstallProblem | null;
  failurePaths: string[];
  onAcknowledgedChange: (acknowledged: boolean) => void;
  onClose: () => void;
  onConfirm: () => void | Promise<void>;
}) {
  const { t } = useI18n();

  return (
    <ConfirmDialog
      titleId="reset-data-modal-title"
      title={t('resetDataConfirmTitle')}
      tone="danger"
      acknowledge={{
        label: t('resetDataConfirmAcknowledge'),
        checked: acknowledged,
        onChange: onAcknowledgedChange
      }}
      confirmLabel={problem ? t('retry') : t('resetDataConfirmAction')}
      busyLabel={t('resetDataRunning')}
      busy={busy}
      activeDismissalPolicy={{ kind: 'blocked' }}
      dismissLabel={problem ? t('close') : undefined}
      onConfirm={onConfirm}
      onClose={onClose}
    >
      <ConfirmTarget>
        {t('resetDataTarget', { path: targetPath })}
      </ConfirmTarget>
      <ConfirmNote tone="danger" icon={<Database size={15} />}>
        <p>{t('resetDataConfirmBody')}</p>
      </ConfirmNote>
      <ConfirmNote tone="neutral" icon={<ShieldCheck size={15} />}>
        <p>{t('resetDataConfirmKeepsInstall')}</p>
        <p>{t('resetDataConfirmGameClosed')}</p>
      </ConfirmNote>
      {problem && <InstallProblemBanner problem={problem} />}
      {failurePaths.length > 0 && (
        <ResetDataFailureDetails paths={failurePaths} />
      )}
    </ConfirmDialog>
  );
}
