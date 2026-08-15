import { useEffect, useState } from 'react';
import { PageShell } from '../components/ui/PageShell';
import { LoadingPanel } from '../components/ui/LoadingPanel';
import { useAppBootstrap } from '../features/about/AppBootstrapProvider';
import { useUpdater } from '../features/about/UpdaterProvider';
import { InstallActionsPanel } from '../features/install/InstallActionsPanel';
import { InstallConfirmModal } from '../features/install/InstallConfirmModal';
import { InstallStatusPanel } from '../features/install/InstallStatusPanel';
import { ResetBepinexConfirmModal } from '../features/install/ResetBepinexConfirmModal';
import { ResetDataConfirmModal } from '../features/install/ResetDataConfirmModal';
import { UninstallConfirmModal } from '../features/install/UninstallConfirmModal';
import { useInstallPage } from '../features/install/useInstallPage';
import { useI18n } from '../i18n/LocaleProvider';
import { InstallProblemBanner } from '../features/install/InstallProblemBanner';
import {
  installFailurePaths,
  presentInstallNotice,
  presentInstallProblem
} from '../features/install/installProblems';
import { ModalSource } from '../components/ui/ModalCoordinator';
import { useToast } from '../components/ui/Toast';

export default function Install() {
  const { t } = useI18n();
  const app = useAppBootstrap();
  const updater = useUpdater();
  const { snapshot, intents } = useInstallPage();
  const { dismissToast, showToast } = useToast();
  const [installAcknowledged, setInstallAcknowledged] = useState(false);
  const [resetDataAcknowledged, setResetDataAcknowledged] = useState(false);
  const [resetBepinexAcknowledged, setResetBepinexAcknowledged] =
    useState(false);
  const appVersion =
    app.resource.data?.app_version ?? app.bootstrap.app_version;
  const confirmation = snapshot.confirmation;
  const confirmationKind = confirmation?.target.kind ?? null;
  const confirmationOpen = confirmation !== null;
  const confirmationRunning = confirmation?.phase === 'running';
  const confirmationFailed =
    confirmation?.phase === 'failed' ? confirmation.problem : null;
  const resetFailurePaths = confirmationFailed
    ? installFailurePaths(confirmationFailed)
    : [];

  useEffect(() => {
    setInstallAcknowledged(false);
    setResetDataAcknowledged(false);
    setResetBepinexAcknowledged(false);
  }, [confirmationKind, confirmationOpen]);

  useEffect(() => {
    if (!snapshot.notice) return;
    const notice = snapshot.notice;
    showToast({
      id: `install:notice:${notice.id}`,
      tone: 'success',
      message: presentInstallNotice(notice.code, t)
    });
    intents.acknowledgeNotice(notice.id);
  }, [intents, showToast, snapshot.notice, t]);

  useEffect(() => {
    if (!snapshot.actionProblem) {
      dismissToast('install:action-problem');
      return;
    }
    showToast({
      id: 'install:action-problem',
      tone: 'error',
      message: presentInstallProblem(snapshot.actionProblem, t)
    });
  }, [dismissToast, showToast, snapshot.actionProblem, t]);

  return (
    <PageShell title={t('installTitle')} className="bpp-install-page">
      {snapshot.phase === 'initial-loading' ? (
        <LoadingPanel label={t('installDetecting')} className="h-64" />
      ) : snapshot.phase === 'blocking-failure' ? (
        <InstallProblemBanner
          problem={snapshot.problem}
          onRetry={() => void intents.refresh()}
        />
      ) : snapshot.phase === 'ready' ? (
        <>
          {snapshot.refresh.phase === 'failed' && (
            <InstallProblemBanner
              problem={snapshot.refresh.problem}
              onRetry={() => void intents.refresh()}
            />
          )}
          {snapshot.reconciliationProblem && (
            <InstallProblemBanner problem={snapshot.reconciliationProblem} />
          )}
          {snapshot.refresh.phase === 'refreshing' && (
            <p
              role="status"
              aria-live="polite"
              className="bpp-install-refreshing m-0 text-xs"
            >
              {t('installRefreshing')}
            </p>
          )}
          <InstallStatusPanel
            snapshot={snapshot}
            intents={intents}
            appVersion={appVersion}
          />
          <InstallActionsPanel
            snapshot={snapshot}
            intents={intents}
            updateChecking={updater.phase === 'checking'}
            onCheckUpdate={updater.checkNow}
          />
        </>
      ) : null}

      <ModalSource
        id="route:install-confirmation"
        open={confirmation?.target.kind === 'install'}
        priority={confirmationRunning ? 'critical' : 'confirmation'}
        dismissalPolicy={confirmationRunning ? 'blocked' : 'dismissible'}
      >
        {confirmation?.target.kind === 'install' &&
          snapshot.phase === 'ready' && (
            <InstallConfirmModal
              busy={confirmationRunning}
              installAcknowledged={installAcknowledged}
              onAcknowledgedChange={setInstallAcknowledged}
              problem={confirmationFailed}
              onClose={() => intents.dismissConfirmation()}
              onConfirm={() => void intents.confirm()}
            />
          )}
      </ModalSource>

      <ModalSource
        id="route:install-reset"
        open={
          confirmation?.target.kind === 'reset-data' ||
          confirmation?.target.kind === 'reset-bepinex' ||
          confirmation?.target.kind === 'uninstall'
        }
        priority={confirmationRunning ? 'critical' : 'confirmation'}
        dismissalPolicy={confirmationRunning ? 'blocked' : 'dismissible'}
      >
        {confirmation?.target.kind === 'reset-data' && (
          <ResetDataConfirmModal
            busy={confirmationRunning}
            acknowledged={resetDataAcknowledged}
            targetPath={confirmation.target.gamePath}
            problem={confirmationFailed}
            failurePaths={resetFailurePaths}
            onAcknowledgedChange={setResetDataAcknowledged}
            onClose={() => intents.dismissConfirmation()}
            onConfirm={() => void intents.confirm()}
          />
        )}

        {confirmation?.target.kind === 'reset-bepinex' && (
          <ResetBepinexConfirmModal
            busy={confirmationRunning}
            acknowledged={resetBepinexAcknowledged}
            targetPath={confirmation.target.gamePath}
            problem={confirmationFailed}
            onAcknowledgedChange={setResetBepinexAcknowledged}
            onClose={() => intents.dismissConfirmation()}
            onConfirm={() => void intents.confirm()}
          />
        )}

        {confirmation?.target.kind === 'uninstall' && (
          <UninstallConfirmModal
            busy={confirmationRunning}
            targetPath={confirmation.target.gamePath}
            problem={confirmationFailed}
            onClose={() => intents.dismissConfirmation()}
            onConfirm={() => void intents.confirm()}
          />
        )}
      </ModalSource>
    </PageShell>
  );
}
