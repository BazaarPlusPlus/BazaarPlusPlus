import { PackageMinus, ShieldCheck } from 'lucide-react';
import { ConfirmDialog } from '../../components/ui/ConfirmDialog';
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
      <p className="bpp-confirm-target m-0 text-[12px] leading-relaxed fira-code selectable break-all">
        {t('uninstallTarget', { path: targetPath })}
      </p>
      <div className="bpp-confirm-note is-danger flex items-start gap-3 p-4">
        <PackageMinus
          size={16}
          className="bpp-confirm-note-icon mt-0.5 shrink-0"
        />
        <p className="m-0 text-[13px] leading-relaxed">
          {t('uninstallConfirmBody')}
        </p>
      </div>
      <div className="bpp-confirm-note is-warning flex items-start gap-3 p-4">
        <ShieldCheck
          size={16}
          className="bpp-confirm-note-icon mt-0.5 shrink-0"
        />
        <p className="m-0 text-[13px] leading-relaxed">
          {t('uninstallConfirmKeepsData')}
        </p>
      </div>
      {problem && <InstallProblemBanner problem={problem} />}
    </ConfirmDialog>
  );
}
