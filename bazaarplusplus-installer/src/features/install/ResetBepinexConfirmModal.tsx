import { FolderX, ShieldCheck } from 'lucide-react';
import { ConfirmDialog } from '../../components/ui/ConfirmDialog';
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
      <p className="bpp-confirm-target m-0 text-[12px] leading-relaxed fira-code selectable break-all">
        {t('resetBepinexTarget', { path: targetPath })}
      </p>
      <div className="bpp-confirm-note is-danger flex items-start gap-3 p-4">
        <FolderX size={16} className="bpp-confirm-note-icon mt-0.5 shrink-0" />
        <div className="flex flex-col gap-2 text-[13px] leading-relaxed">
          <p className="m-0">{t('resetBepinexConfirmBody')}</p>
          <p className="m-0">{t('resetBepinexConfirmOtherMods')}</p>
        </div>
      </div>

      <div className="bpp-confirm-note is-warning flex items-start gap-3 p-4">
        <ShieldCheck
          size={16}
          className="bpp-confirm-note-icon mt-0.5 shrink-0"
        />
        <div className="flex flex-col gap-2 text-[13px] leading-relaxed">
          <p className="m-0">{t('resetBepinexConfirmReinstall')}</p>
          <p className="m-0">{t('resetBepinexConfirmGameClosed')}</p>
        </div>
      </div>
      {problem && <InstallProblemBanner problem={problem} />}
    </ConfirmDialog>
  );
}
