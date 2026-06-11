import { Download, LoaderCircle, RefreshCw } from 'lucide-react';
import { Dialog } from '../components/ui/Dialog';
import type { UpdaterController } from '../features/about/useUpdater';
import { useI18n } from '../i18n/LocaleProvider';
import type { MessageKey } from '../i18n/messages';

type ShellUpdateModalProps = {
  updater: UpdaterController;
};

const PHASE_TITLES: Partial<Record<UpdaterController['phase'], MessageKey>> = {
  available: 'updateModalTitle',
  downloading: 'updateDownloading',
  installing: 'updateInstalling',
  ready: 'updateReady',
  error: 'updateError'
};

function formatMegabytes(bytes: number): string {
  return (bytes / (1024 * 1024)).toFixed(1);
}

export function ShellUpdateModal({ updater }: ShellUpdateModalProps) {
  const { t } = useI18n();
  // downloadAndInstall cannot be cancelled cleanly, so the modal is not
  // dismissable while it runs.
  const dismissable =
    updater.phase !== 'downloading' && updater.phase !== 'installing';

  const laterButton = (
    <button
      type="button"
      onClick={updater.dismiss}
      className="h-9 px-4 border border-[rgba(200,148,55,0.22)] rounded-[2px] text-[11px] uppercase text-[rgba(232,220,200,0.72)] transition-colors hover:border-[rgba(200,148,55,0.38)]"
    >
      {t('updateModalLater')}
    </button>
  );

  return (
    <Dialog
      onClose={dismissable ? updater.dismiss : () => undefined}
      labelledBy="update-modal-title"
    >
      <div className="w-[min(460px,calc(100vw-32px))] border border-[rgba(200,148,55,0.26)] bg-[#130d08] shadow-[0_24px_70px_rgba(0,0,0,0.58)]">
        <div className="border-b border-[rgba(200,148,55,0.18)] px-6 py-5">
          <div className="flex items-start gap-4">
            <div className="flex size-10 items-center justify-center rounded-[2px] border border-[rgba(200,148,55,0.28)] bg-[rgba(200,148,55,0.1)] text-[rgba(232,212,174,0.9)]">
              {updater.phase === 'downloading' ||
              updater.phase === 'installing' ? (
                <LoaderCircle size={18} className="animate-spin" />
              ) : updater.phase === 'ready' ? (
                <RefreshCw size={18} />
              ) : (
                <Download size={18} />
              )}
            </div>
            <div>
              <p className="m-0 cinzel text-[10px] uppercase text-[rgba(200,170,120,0.68)]">
                {t('updateModalKicker')}
              </p>
              <h2
                id="update-modal-title"
                className="m-0 mt-2 cinzel text-xl leading-tight text-[#f2e4c8]"
              >
                {t(PHASE_TITLES[updater.phase] ?? 'updateModalTitle')}
              </h2>
            </div>
          </div>
        </div>

        <div className="px-6 py-5">
          {updater.phase === 'available' && (
            <>
              <p className="m-0 text-sm leading-6 text-[rgba(232,220,200,0.82)]">
                {t('updateModalBody', { version: updater.version ?? '' })}
              </p>
              {updater.notes && (
                <div className="mt-4">
                  <p className="m-0 cinzel text-[10px] uppercase text-[rgba(200,170,120,0.68)]">
                    {t('updateNotesLabel')}
                  </p>
                  <p className="m-0 mt-2 max-h-44 overflow-y-auto whitespace-pre-wrap text-[13px] leading-6 text-[rgba(232,220,200,0.72)]">
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
            <p className="m-0 text-sm leading-6 text-[rgba(232,220,200,0.82)]">
              {t('updateModalBody', { version: updater.version ?? '' })}
            </p>
          )}

          {updater.phase === 'ready' && updater.error && (
            <p
              className="m-0 text-sm leading-6 text-[rgba(220,140,120,0.9)] break-words line-clamp-3"
              title={updater.error}
            >
              {updater.error}
            </p>
          )}

          {updater.phase === 'error' && (
            <p
              className="m-0 text-sm leading-6 text-[rgba(220,140,120,0.9)] break-words line-clamp-3"
              title={updater.error ?? undefined}
            >
              {updater.error}
            </p>
          )}
        </div>

        {dismissable && (
          <div className="flex justify-end gap-3 border-t border-[rgba(200,148,55,0.14)] px-6 py-4">
            {updater.phase === 'available' && (
              <>
                {laterButton}
                <button
                  type="button"
                  onClick={updater.install}
                  className="inline-flex h-9 items-center gap-2 rounded-[2px] border border-[rgba(255,198,98,0.38)] bg-[rgba(200,148,55,0.16)] px-4 cinzel text-[11px] uppercase text-[#f2e4c8] transition-colors hover:bg-[rgba(200,148,55,0.24)]"
                >
                  <Download size={14} />
                  {t('updateInstall')}
                </button>
              </>
            )}
            {updater.phase === 'ready' && (
              <button
                type="button"
                onClick={updater.restart}
                className="inline-flex h-9 items-center gap-2 rounded-[2px] border border-[rgba(255,198,98,0.38)] bg-[rgba(200,148,55,0.16)] px-4 cinzel text-[11px] uppercase text-[#f2e4c8] transition-colors hover:bg-[rgba(200,148,55,0.24)]"
              >
                <RefreshCw size={14} />
                {t('updateRestartNow')}
              </button>
            )}
            {updater.phase === 'error' && (
              <>
                {laterButton}
                <button
                  type="button"
                  onClick={updater.install}
                  className="inline-flex h-9 items-center gap-2 rounded-[2px] border border-[rgba(255,198,98,0.38)] bg-[rgba(200,148,55,0.16)] px-4 cinzel text-[11px] uppercase text-[#f2e4c8] transition-colors hover:bg-[rgba(200,148,55,0.24)]"
                >
                  <RefreshCw size={14} />
                  {t('updateRetry')}
                </button>
              </>
            )}
          </div>
        )}
      </div>
    </Dialog>
  );
}

function UpdateDownloadProgress({
  progress
}: {
  progress: UpdaterController['progress'];
}) {
  const downloaded = progress?.downloaded ?? 0;
  const total = progress?.total ?? null;
  const percent =
    total && total > 0
      ? Math.min(100, Math.round((downloaded / total) * 100))
      : null;

  return (
    <div>
      <div className="h-1.5 w-full overflow-hidden rounded-[2px] bg-[rgba(200,148,55,0.14)]">
        <div
          className={`h-full bg-[rgba(228,178,88,0.85)] transition-[width] duration-200 ${
            percent === null ? 'w-1/3 animate-pulse' : ''
          }`}
          style={percent === null ? undefined : { width: `${percent}%` }}
        />
      </div>
      <p className="m-0 mt-3 text-[12px] tabular-nums text-[rgba(232,220,200,0.72)]">
        {percent === null
          ? `${formatMegabytes(downloaded)} MB`
          : `${formatMegabytes(downloaded)} / ${formatMegabytes(total ?? 0)} MB (${percent}%)`}
      </p>
    </div>
  );
}
