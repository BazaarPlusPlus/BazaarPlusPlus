import { PackageMinus, ShieldCheck } from 'lucide-react';
import {
  ConfirmDialog,
  ConfirmNote,
  ConfirmTarget
} from '../../components/ui/ConfirmDialog';
import { useI18n } from '../../i18n/LocaleProvider';
import { InstallProblemBanner } from './InstallProblemBanner';
import type { InstallProblem } from './installProblems';

export function UninstallConfirmModal({
  busy,
  targetPath,
  problem,
  onClose,
  onConfirm
}: {
  busy: boolean;
  targetPath: string;
  problem: InstallProblem | null;
  onClose: () => void;
  onConfirm: () => void | Promise<void>;
}) {
  const { t } = useI18n();

  return (
    <ConfirmDialog
      titleId="uninstall-modal-title"
      title={t('uninstallConfirmTitle')}
      tone="danger"
      confirmLabel={problem ? t('retry') : t('uninstallConfirmAction')}
      busyLabel={t('uninstallRunning')}
      busy={busy}
      activeDismissalPolicy={{ kind: 'blocked' }}
      dismissLabel={problem ? t('close') : undefined}
      onConfirm={onConfirm}
      onClose={onClose}
    >
      <ConfirmTarget>
        {t('uninstallTarget', { path: targetPath })}
      </ConfirmTarget>
      <ConfirmNote tone="danger" icon={<PackageMinus size={15} />}>
        <p>{t('uninstallConfirmBody')}</p>
      </ConfirmNote>
      <ConfirmNote tone="neutral" icon={<ShieldCheck size={15} />}>
        <p>{t('uninstallConfirmKeepsData')}</p>
      </ConfirmNote>
      {problem && <InstallProblemBanner problem={problem} />}
    </ConfirmDialog>
  );
}
