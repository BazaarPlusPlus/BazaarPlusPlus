import { BookOpen, ExternalLink, TriangleAlert } from 'lucide-react';
import { ConfirmDialog, ConfirmNote } from '../../components/ui/ConfirmDialog';
import { useI18n } from '../../i18n/LocaleProvider';
import { isWindowsPlatform } from '../shared/platform';
import { InstallProblemBanner } from './InstallProblemBanner';
import type { InstallProblem } from './installProblems';

export function InstallConfirmModal({
  busy,
  willCloseSteam,
  installAcknowledged,
  onAcknowledgedChange,
  problem,
  onClose,
  onConfirm
}: {
  busy: boolean;
  willCloseSteam: boolean;
  installAcknowledged: boolean;
  onAcknowledgedChange: (acknowledged: boolean) => void;
  problem: InstallProblem | null;
  onClose: () => void;
  onConfirm: () => void | Promise<void>;
}) {
  const { t } = useI18n();
  return (
    <ConfirmDialog
      titleId="install-modal-title"
      title={t('installModalTitle')}
      tone="primary"
      acknowledge={{
        label: t('installAcknowledge'),
        checked: installAcknowledged,
        onChange: onAcknowledgedChange
      }}
      confirmLabel={problem ? t('retry') : t('confirmInstall')}
      busyLabel={t('installing')}
      busy={busy}
      activeDismissalPolicy={{ kind: 'blocked' }}
      dismissLabel={problem ? t('close') : undefined}
      onConfirm={onConfirm}
      onClose={onClose}
    >
      <ConfirmNote tone="neutral" icon={<BookOpen size={15} />}>
        <p className="text-fg-1">{t('tutorialKicker')}</p>
        <p>{t('installModalBody')}</p>
        <a
          href="https://bazaarplusplus.com/tutorial"
          target="_blank"
          rel="noreferrer"
          className="bpp-dialog-link"
        >
          {t('viewTutorial')}
          <ExternalLink size={13} />
        </a>
      </ConfirmNote>

      <ConfirmNote tone="warning" icon={<TriangleAlert size={15} />}>
        <p>
          {t(
            isWindowsPlatform()
              ? 'installSteamNotice'
              : willCloseSteam
                ? 'installSteamNoticeMacos'
                : 'installCloseGameNotice'
          )}
        </p>
      </ConfirmNote>

      {problem && <InstallProblemBanner problem={problem} />}
    </ConfirmDialog>
  );
}
