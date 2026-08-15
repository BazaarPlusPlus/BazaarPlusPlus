import {
  CloudDownload,
  Download,
  ExternalLink,
  LoaderCircle,
  RefreshCw
} from 'lucide-react';
import { openUrl } from '@tauri-apps/plugin-opener';
import { hasTauriRuntime } from '../api/runtime';
import { Dialog } from '../components/ui/Dialog';
import { ProblemBanner } from '../components/ui/ProblemBanner';
import { getMainlandDownloadUrl } from '../features/about/mainlandDownload';
import type { UpdaterController } from '../features/about/useUpdater';
import type { UpdaterUiContract } from '../features/about/updaterPresentation';
import { presentUpdaterProblem } from '../features/about/updaterProblems';
import { formatProblemDiagnostic } from '../features/shared/problems';
import { useI18n } from '../i18n/LocaleProvider';

type ShellUpdateModalProps = {
  updater: UpdaterController;
  presentation: NonNullable<UpdaterUiContract['modal']>;
};

function formatMegabytes(bytes: number): string {
  return (bytes / (1024 * 1024)).toFixed(1);
}

export function ShellUpdateModal({
  updater,
  presentation
}: ShellUpdateModalProps) {
  const { locale, t } = useI18n();
  const dismissible = presentation.dismissalPolicy === 'dismissible';
  const action = presentation.action;
  const actionHandler =
    action === 'install' || action === 'retry-install'
      ? updater.install
      : updater.restart;
  // The mainland mirror only helps users on that side of the network, and the
  // zh locale is the closest signal the frontend has for them.
  const mainlandDownloadUrl =
    updater.version && locale === 'zh'
      ? getMainlandDownloadUrl(updater.version)
      : null;

  const openMainlandDownload = (event: React.MouseEvent<HTMLAnchorElement>) => {
    if (!hasTauriRuntime() || !mainlandDownloadUrl) return;
    event.preventDefault();
    void openUrl(mainlandDownloadUrl).catch((error: unknown) => {
      console.error('Failed to open the mainland installer download.', error);
    });
  };

  return (
    <Dialog
      onClose={updater.dismiss}
      labelledBy="update-modal-title"
      focusContainerOnOpen
    >
      <div className="bpp-modal-card w-[min(460px,calc(100vw-32px))]">
        <div className="bpp-update-modal-header px-6 py-5">
          <div className="flex items-start gap-4">
            <div className="bpp-update-modal-icon flex size-10 items-center justify-center">
              {updater.phase === 'downloading' ||
              updater.phase === 'installing' ||
              updater.phase === 'restarting' ? (
                <LoaderCircle size={18} className="animate-spin" />
              ) : updater.phase === 'ready-to-restart' ||
                (updater.phase === 'failed' &&
                  updater.problem.code === 'updater_restart_failed') ? (
                <RefreshCw size={18} />
              ) : (
                <Download size={18} />
              )}
            </div>
            <div>
              <p className="bpp-update-modal-kicker m-0 cinzel text-[10px] uppercase">
                {t('updateModalKicker')}
              </p>
              <h2
                id="update-modal-title"
                className="bpp-update-modal-title m-0 mt-2 cinzel text-xl leading-tight"
              >
                {t(presentation.titleKey)}
              </h2>
            </div>
          </div>
        </div>

        <div className="px-6 py-5">
          {updater.phase === 'available' && (
            <>
              <p className="bpp-update-modal-copy m-0 text-sm leading-6">
                {t('updateModalBody', { version: updater.version })}
              </p>
              {mainlandDownloadUrl && (
                <div className="bpp-update-modal-mainland mt-4">
                  <span
                    className="bpp-update-modal-mainland-icon"
                    aria-hidden="true"
                  >
                    <CloudDownload size={16} />
                  </span>
                  <div className="bpp-update-modal-mainland-copy">
                    <p className="bpp-update-modal-mainland-title m-0">
                      {t('updateMainlandDownloadTitle')}
                    </p>
                    <p className="bpp-update-modal-mainland-description m-0">
                      {t('updateMainlandDownloadHint')}
                    </p>
                  </div>
                  <a
                    href={mainlandDownloadUrl}
                    target="_blank"
                    rel="noopener noreferrer"
                    onClick={openMainlandDownload}
                    className="bpp-update-modal-mainland-link inline-flex items-center gap-1.5 whitespace-nowrap transition-colors"
                  >
                    {t('updateMainlandDownload')}
                    <ExternalLink size={12} aria-hidden="true" />
                  </a>
                </div>
              )}
              {updater.notes && (
                <div className="mt-4">
                  <p className="bpp-update-modal-kicker m-0 cinzel text-[10px] uppercase">
                    {t('updateNotesLabel')}
                  </p>
                  <p className="bpp-update-modal-notes m-0 mt-2 max-h-44 overflow-y-auto whitespace-pre-wrap text-[13px] leading-6">
                    {updater.notes}
                  </p>
                </div>
              )}
            </>
          )}

          {updater.phase === 'downloading' && (
            <UpdateDownloadProgress progress={updater.progress} />
          )}

          {updater.phase === 'installing' && (
            <p
              role="status"
              aria-live="polite"
              className="bpp-update-modal-copy m-0 text-sm leading-6"
            >
              {t('updateInstallingBody', { version: updater.version })}
            </p>
          )}

          {updater.phase === 'ready-to-restart' && (
            <p className="bpp-update-modal-copy m-0 text-sm leading-6">
              {t('updateReadyBody', { version: updater.version })}
            </p>
          )}

          {updater.phase === 'restarting' && (
            <p
              role="status"
              aria-live="polite"
              className="bpp-update-modal-copy m-0 text-sm leading-6"
            >
              {t('updateRestarting')}
            </p>
          )}

          {updater.phase === 'failed' && (
            <ProblemBanner
              message={presentUpdaterProblem(updater.problem, t)}
              diagnostic={
                updater.problem.diagnostic
                  ? formatProblemDiagnostic(updater.problem)
                  : null
              }
              diagnosticLabel={t('problemDiagnostics')}
            />
          )}
        </div>

        {dismissible && (
          <div className="bpp-update-modal-footer flex justify-end gap-3 px-6 py-4">
            <button
              type="button"
              onClick={updater.dismiss}
              className="bpp-update-modal-later h-9 px-4 text-[11px] uppercase transition-colors"
            >
              {t('updateModalLater')}
            </button>
            {action && presentation.actionLabelKey && (
              <button
                type="button"
                onClick={actionHandler}
                className="bpp-update-modal-action inline-flex h-9 items-center gap-2 px-4 cinzel text-[11px] uppercase transition-colors"
              >
                {action === 'install' ? (
                  <Download size={14} />
                ) : (
                  <RefreshCw size={14} />
                )}
                {t(presentation.actionLabelKey)}
              </button>
            )}
          </div>
        )}
      </div>
    </Dialog>
  );
}

export function UpdateDownloadProgress({
  progress
}: {
  progress: UpdaterController['progress'];
}) {
  const { t } = useI18n();
  const downloaded = progress?.downloaded ?? 0;
  const total = progress?.total ?? null;
  const percent =
    total && total > 0
      ? Math.min(100, Math.round((downloaded / total) * 100))
      : null;
  const accessibleValue =
    total === null ? undefined : Math.min(downloaded, total);
  const status =
    percent === null
      ? t('updateDownloadProgressUnknown', {
          downloaded: formatMegabytes(downloaded)
        })
      : t('updateDownloadProgressKnown', {
          downloaded: formatMegabytes(downloaded),
          total: formatMegabytes(total ?? 0),
          percent
        });

  return (
    <div>
      <div
        role="progressbar"
        aria-label={t('updateDownloadProgressLabel')}
        aria-valuemin={0}
        aria-valuemax={total ?? undefined}
        aria-valuenow={accessibleValue}
        aria-valuetext={status}
        className="bpp-update-progress-track h-1.5 w-full overflow-hidden"
      >
        <div
          className={`bpp-update-progress-value h-full transition-[width] duration-200 ${
            percent === null ? 'w-1/3 animate-pulse' : ''
          }`}
          style={percent === null ? undefined : { width: `${percent}%` }}
        />
      </div>
      <p
        role="status"
        aria-live="polite"
        className="bpp-update-progress-status m-0 mt-3 text-[12px] tabular-nums"
      >
        {status}
      </p>
    </div>
  );
}
