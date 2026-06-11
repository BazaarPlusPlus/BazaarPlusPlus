import {
  AlertCircle,
  AlertTriangle,
  DownloadCloud,
  Loader2,
  Play,
  RefreshCw,
  Trash2,
  XCircle
} from 'lucide-react';
import type { useInstallPage } from './useInstallPage';
import { InstallActionButton } from './InstallActionButton';
import { InstallFactItem } from './InstallFactItem';
import { ResetDataFailureDetails } from './ResetDataFailureDetails';
import { useI18n } from '../../i18n/LocaleProvider';

type InstallPage = ReturnType<typeof useInstallPage>;

export function InstallActionsPanel({
  page,
  primaryMode,
  onOpenInstallModal,
  onOpenResetDataModal
}: {
  page: InstallPage;
  primaryMode: 'install' | 'reinstall' | 'launch';
  onOpenInstallModal: () => void;
  onOpenResetDataModal: () => void;
}) {
  const { t } = useI18n();
  return (
    <div className="col-span-5 flex flex-col gap-6">
      <div className="p-5 bg-[rgba(18,11,5,0.88)] border border-[rgba(180,130,48,0.13)] rounded-sm shadow-[0_6px_28px_rgba(0,0,0,0.35)] flex flex-col gap-5 h-full">
        <section className="flex-1 flex flex-col">
          <h3 className="cinzel text-xs tracking-widest text-[rgba(220,195,145,0.8)] mb-3 uppercase">
            {t('installActionsHeading')}
          </h3>
          <div className="flex flex-col gap-5 flex-1">
            <div className="text-center pb-2">
              <PrimaryActionButton
                page={page}
                primaryMode={primaryMode}
                onOpenInstallModal={onOpenInstallModal}
              />
            </div>

            <div className="h-px bg-gradient-to-r from-transparent via-[rgba(200,148,55,0.3)] to-transparent" />

            <ul className="flex flex-col gap-2">
              <InstallFactItem
                label="BazaarPlusPlus"
                value={
                  page.state.mod_state.installed
                    ? t('installed')
                    : t('notInstalled')
                }
              />
            </ul>

            {page.state.warnings.length > 0 && (
              <div
                className="flex flex-col gap-2"
                role="status"
                aria-live="polite"
              >
                {page.state.warnings.map((warning) => (
                  <p
                    key={warning.code}
                    className="m-0 flex items-start gap-2 text-xs text-[rgba(232,190,120,0.82)]"
                  >
                    <AlertCircle size={14} className="mt-0.5 shrink-0" />
                    <span>{warning.message}</span>
                  </p>
                ))}
              </div>
            )}

            {(page.error || page.message) && (
              <p
                role={page.error ? 'alert' : 'status'}
                aria-live={page.error ? 'assertive' : 'polite'}
                className={`m-0 text-xs selectable ${page.error ? 'text-[#d96d6d]' : 'text-[#6dd9a0]'}`}
              >
                {page.error ?? page.message}
              </p>
            )}
            {page.resetDataFailurePaths.length > 0 && (
              <ResetDataFailureDetails paths={page.resetDataFailurePaths} />
            )}

            <div className="h-px bg-gradient-to-r from-transparent via-[rgba(200,148,55,0.3)] to-transparent" />

            <div className="grid grid-cols-2 gap-2 mt-auto pt-2">
              <InstallActionButton
                disabled={page.busy || !page.state.actions.can_reset_data}
                busy={page.action === 'resetData'}
                onClick={onOpenResetDataModal}
                icon={<AlertTriangle size={14} />}
                label={
                  page.state.game.path_valid && !page.state.has_resettable_data
                    ? t('actionNoResettableData')
                    : t('actionResetData')
                }
                danger
              />
              <InstallActionButton
                disabled={page.busy || !page.state.actions.can_uninstall}
                busy={page.action === 'uninstall'}
                onClick={page.uninstall}
                icon={<Trash2 size={14} />}
                label={t('actionUninstall')}
                danger
              />
            </div>
          </div>
        </section>
      </div>
    </div>
  );
}

function PrimaryActionButton({
  page,
  primaryMode,
  onOpenInstallModal
}: {
  page: InstallPage;
  primaryMode: 'install' | 'reinstall' | 'launch';
  onOpenInstallModal: () => void;
}) {
  const { t } = useI18n();
  if (primaryMode === 'launch') {
    return (
      <div className="flex flex-col gap-2">
        <button
          type="button"
          disabled={!page.state.actions.can_launch || page.action === 'launch'}
          onClick={page.launch}
          className="w-full py-4 bg-gradient-to-b from-[#d4a040] to-[#9e5c1e] text-[#0b0906] font-bold cinzel tracking-wider rounded-sm shadow-[0_0_15px_rgba(212,160,64,0.4)] hover:brightness-110 active:brightness-95 disabled:opacity-45 disabled:hover:brightness-100 transition-all flex items-center justify-center gap-2 text-lg"
        >
          {page.action === 'launch' ? (
            <Loader2 size={20} className="animate-spin" />
          ) : (
            <Play size={20} fill="currentColor" />
          )}
          {t('launchGame')}
        </button>
        {page.state.launch_flow === 'tempo' && (
          <p className="m-0 text-xs text-[rgba(232,190,120,0.78)]">
            {t('tempoLaunchHint')}
          </p>
        )}
        {page.action === 'launch' && page.state.launch_flow === 'tempo' && (
          <InstallActionButton
            icon={<XCircle size={14} />}
            label={t('tempoCancelLaunch')}
            onClick={page.cancelLaunch}
            className="w-full"
            danger
          />
        )}
      </div>
    );
  }

  if (primaryMode === 'reinstall') {
    return (
      <button
        type="button"
        disabled={
          !page.state.actions.can_reinstall || page.action === 'install'
        }
        onClick={onOpenInstallModal}
        className="w-full py-4 bg-gradient-to-b from-[#d24a4a] to-[#8e1e1e] text-[#fdeaea] font-bold cinzel tracking-wider rounded-sm shadow-[0_0_15px_rgba(200,60,60,0.4)] hover:brightness-110 active:brightness-95 disabled:opacity-45 disabled:hover:brightness-100 transition-all flex items-center justify-center gap-2 text-lg"
      >
        {page.action === 'install' ? (
          <Loader2 size={20} className="animate-spin" />
        ) : (
          <RefreshCw size={20} />
        )}
        {t('actionReinstall')}
      </button>
    );
  }

  return (
    <button
      type="button"
      disabled={!page.state.actions.can_install || page.action === 'install'}
      onClick={onOpenInstallModal}
      className="w-full py-4 bg-gradient-to-b from-[#d4a040] to-[#9e5c1e] text-[#0b0906] font-bold cinzel tracking-wider rounded-sm shadow-[0_0_15px_rgba(212,160,64,0.4)] hover:brightness-110 active:brightness-95 disabled:opacity-45 disabled:hover:brightness-100 transition-all flex items-center justify-center gap-2 text-lg"
    >
      {page.action === 'install' ? (
        <Loader2 size={20} className="animate-spin" />
      ) : (
        <DownloadCloud size={20} />
      )}
      {t('actionInstall')}
    </button>
  );
}
