import {
  CloudDownload,
  Download,
  ExternalLink,
  LoaderCircle,
  RefreshCw
} from 'lucide-react';
import { openUrl } from '@tauri-apps/plugin-opener';
import { hasTauriRuntime } from '../api/runtime';
import { Button } from '../components/ui/Button';
import { ConfirmNote } from '../components/ui/ConfirmDialog';
import {
  Dialog,
  DialogBody,
  DialogCard,
  DialogFooter,
  DialogHeader
} from '../components/ui/Dialog';
import { ProblemBanner } from '../components/ui/ProblemBanner';
import { getMainlandDownloadUrl } from '../features/about/mainlandDownload';
import type { UpdaterController } from '../features/about/useUpdater';
import type { UpdaterUiContract } from '../features/about/updaterPresentation';
import { presentUpdaterProblem } from '../features/about/updaterProblems';
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
      <DialogCard>
        <DialogHeader
          titleId="update-modal-title"
          title={t(presentation.titleKey)}
          tone="accent"
          icon={
            updater.phase === 'downloading' ||
            updater.phase === 'installing' ||
            updater.phase === 'restarting' ? (
              <LoaderCircle size={18} className="bpp-spin" />
            ) : updater.phase === 'ready-to-restart' ||
              (updater.phase === 'failed' &&
                updater.problem.code === 'updater_restart_failed') ? (
              <RefreshCw size={18} />
            ) : (
              <Download size={18} />
            )
          }
        />

        <DialogBody>
          {updater.phase === 'available' && (
            <>
              <p className="text-fg-1">
                {t('updateModalBody', { version: updater.version })}
              </p>
              {mainlandDownloadUrl && (
                <ConfirmNote tone="neutral" icon={<CloudDownload size={15} />}>
                  <p className="text-fg-1">
                    {t('updateMainlandDownloadTitle')}
                  </p>
                  <p>{t('updateMainlandDownloadHint')}</p>
                  <a
                    href={mainlandDownloadUrl}
                    target="_blank"
                    rel="noopener noreferrer"
                    onClick={openMainlandDownload}
                    className="bpp-dialog-link"
                  >
                    {t('updateMainlandDownload')}
                    <ExternalLink size={12} aria-hidden="true" />
                  </a>
                </ConfirmNote>
              )}
              {updater.notes && (
                <div className="flex flex-col gap-1.5">
                  <p className="text-fg-3 text-xs font-semibold">
                    {t('updateNotesLabel')}
                  </p>
                  <p className="bpp-confirm-target max-h-44 overflow-y-auto whitespace-pre-wrap font-sans text-[13px] leading-6 text-fg-2">
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
            <p role="status" aria-live="polite">
              {t('updateInstallingBody', { version: updater.version })}
            </p>
          )}

          {updater.phase === 'ready-to-restart' && (
            <p>{t('updateReadyBody', { version: updater.version })}</p>
          )}

          {updater.phase === 'restarting' && (
            <p role="status" aria-live="polite">
              {t('updateRestarting')}
            </p>
          )}

          {updater.phase === 'failed' && (
            <ProblemBanner
              message={presentUpdaterProblem(updater.problem, t)}
              problem={updater.problem}
            />
          )}
        </DialogBody>

        {dismissible && (
          <DialogFooter>
            <Button variant="ghost" onClick={updater.dismiss}>
              {t('updateModalLater')}
            </Button>
            {action && presentation.actionLabelKey && (
              <Button
                variant="primary"
                onClick={actionHandler}
                icon={action === 'install' ? <Download /> : <RefreshCw />}
              >
                {t(presentation.actionLabelKey)}
              </Button>
            )}
          </DialogFooter>
        )}
      </DialogCard>
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
        className="h-1.5 w-full overflow-hidden rounded-full bg-hover"
      >
        <div
          className={`h-full rounded-full bg-accent transition-[width] duration-200 ${
            percent === null ? 'w-1/3 animate-pulse' : ''
          }`}
          style={percent === null ? undefined : { width: `${percent}%` }}
        />
      </div>
      <p
        role="status"
        aria-live="polite"
        className="m-0 mt-3 text-xs tabular-nums text-fg-2"
      >
        {status}
      </p>
    </div>
  );
}
