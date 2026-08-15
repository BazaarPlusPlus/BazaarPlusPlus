import { BookOpen, ExternalLink, TriangleAlert } from 'lucide-react';
import { ConfirmDialog } from '../../components/ui/ConfirmDialog';
import { useI18n } from '../../i18n/LocaleProvider';
import { isWindowsPlatform } from '../shared/platform';
import { InstallProblemBanner } from './InstallProblemBanner';
import type { InstallProblem } from './installProblems';

export function InstallConfirmModal({
  busy,
  installAcknowledged,
  onAcknowledgedChange,
  problem,
  onClose,
  onConfirm
}: {
  busy: boolean;
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
      subtitle={t('installModalSubtitle')}
      tone="gold"
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
      <section className="bpp-install-guide-card">
        <BookOpen
          size={26}
          strokeWidth={1.55}
          className="bpp-install-card-icon"
        />
        <div className="bpp-install-card-copy">
          <p className="bpp-install-card-title">{t('tutorialKicker')}</p>
          <p className="bpp-install-card-description">
            {t('installModalBody')}
          </p>
        </div>
        <a
          href="https://bazaarplusplus.com/tutorial"
          target="_blank"
          rel="noreferrer"
          className="bpp-install-guide-link"
        >
          {t('viewTutorial')}
          <ExternalLink size={14} />
        </a>
      </section>

      <section className="bpp-install-steam-warning">
        <TriangleAlert
          size={34}
          strokeWidth={1.55}
          className="bpp-install-warning-icon"
        />
        <p>
          {t(
            isWindowsPlatform()
              ? 'installSteamNotice'
              : 'installSteamNoticeMacos'
          )}
        </p>
      </section>

      {problem && <InstallProblemBanner problem={problem} />}
    </ConfirmDialog>
  );
}
