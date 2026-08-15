import { Database, ShieldCheck } from 'lucide-react';
import { ConfirmDialog } from '../../components/ui/ConfirmDialog';
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
      <p className="bpp-confirm-target m-0 text-[12px] leading-relaxed fira-code selectable break-all">
        {t('resetDataTarget', { path: targetPath })}
      </p>
      <div className="bpp-confirm-note is-danger flex items-start gap-3 p-4">
        <Database size={16} className="bpp-confirm-note-icon mt-0.5 shrink-0" />
        <p className="m-0 text-[13px] leading-relaxed">
          {t('resetDataConfirmBody')}
        </p>
      </div>

      <div className="bpp-confirm-note is-warning flex items-start gap-3 p-4">
        <ShieldCheck
          size={16}
          className="bpp-confirm-note-icon mt-0.5 shrink-0"
        />
        <div className="flex flex-col gap-2 text-[13px] leading-relaxed">
          <p className="m-0">{t('resetDataConfirmKeepsInstall')}</p>
          <p className="m-0">{t('resetDataConfirmGameClosed')}</p>
        </div>
      </div>
      {problem && <InstallProblemBanner problem={problem} />}
      {failurePaths.length > 0 && (
        <ResetDataFailureDetails paths={failurePaths} />
      )}
    </ConfirmDialog>
  );
}
