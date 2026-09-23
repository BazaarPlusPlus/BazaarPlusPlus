import { FolderX, ShieldCheck } from 'lucide-react';
import {
  ConfirmDialog,
  ConfirmNote,
  ConfirmTarget
} from '../../components/ui/ConfirmDialog';
import { useI18n } from '../../i18n/LocaleProvider';
import { InstallProblemBanner } from './InstallProblemBanner';
import type { InstallProblem } from './installProblems';

export function ResetBepinexConfirmModal({
  busy,
  acknowledged,
  targetPath,
  problem,
  onAcknowledgedChange,
  onClose,
  onConfirm
}: {
  busy: boolean;
  acknowledged: boolean;
  targetPath: string;
  problem: InstallProblem | null;
  onAcknowledgedChange: (acknowledged: boolean) => void;
  onClose: () => void;
  onConfirm: () => void | Promise<void>;
}) {
  const { t } = useI18n();

  return (
    <ConfirmDialog
      titleId="reset-bepinex-modal-title"
      title={t('resetBepinexConfirmTitle')}
      tone="danger"
      acknowledge={{
        label: t('resetBepinexConfirmAcknowledge'),
        checked: acknowledged,
        onChange: onAcknowledgedChange
      }}
      confirmLabel={problem ? t('retry') : t('resetBepinexConfirmAction')}
      busyLabel={t('resetBepinexRunning')}
      busy={busy}
      activeDismissalPolicy={{ kind: 'blocked' }}
      dismissLabel={problem ? t('close') : undefined}
      onConfirm={onConfirm}
      onClose={onClose}
    >
      <ConfirmTarget>
        {t('resetBepinexTarget', { path: targetPath })}
      </ConfirmTarget>
      <ConfirmNote tone="danger" icon={<FolderX size={15} />}>
        <p>{t('resetBepinexConfirmBody')}</p>
        <p>{t('resetBepinexConfirmOtherMods')}</p>
      </ConfirmNote>
      <ConfirmNote tone="neutral" icon={<ShieldCheck size={15} />}>
        <p>{t('resetBepinexConfirmReinstall')}</p>
        <p>{t('resetBepinexConfirmGameClosed')}</p>
      </ConfirmNote>
      {problem && <InstallProblemBanner problem={problem} />}
    </ConfirmDialog>
  );
}
