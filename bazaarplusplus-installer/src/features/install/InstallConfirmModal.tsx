import { AlertCircle, DownloadCloud, ExternalLink, X } from 'lucide-react';
import type { useInstallPage } from './useInstallPage';
import type { InstallCompatState } from '../../types/backend';
import { Dialog } from '../../components/ui/Dialog';
import { useI18n } from '../../i18n/LocaleProvider';

type InstallPage = ReturnType<typeof useInstallPage>;

export function InstallConfirmModal({
  page,
  installAcknowledged,
  onAcknowledgedChange,
  compat,
  compatOptIn,
  onCompatOptInChange,
  onClose,
  onConfirm
}: {
  page: InstallPage;
  installAcknowledged: boolean;
  onAcknowledgedChange: (acknowledged: boolean) => void;
  compat: InstallCompatState;
  compatOptIn: boolean;
  onCompatOptInChange: (value: boolean) => void;
  onClose: () => void;
  onConfirm: () => void | Promise<void>;
}) {
  const { t } = useI18n();
  const showCompatToggle = compat.mode_available || compat.forced;
  return (
    <Dialog onClose={onClose} labelledBy="install-modal-title">
      <div className="bg-[#0b0906] border border-[rgba(200,148,55,0.18)] rounded-[4px] shadow-[0_24px_64px_rgba(0,0,0,0.5)] w-full max-w-md mx-4 relative">
        <div className="flex justify-between items-center px-5 py-4 border-b border-[rgba(200,148,55,0.15)] bg-[rgba(200,148,55,0.02)]">
          <div className="flex items-center gap-3">
            <DownloadCloud size={18} className="text-[rgba(200,148,55,0.8)]" />
            <h2
              id="install-modal-title"
              className="cinzel text-[1.1rem] text-[#e8dcc8] m-0 tracking-wider"
            >
              {t('installModalTitle')}
            </h2>
          </div>
          <button
            type="button"
            onClick={onClose}
            className="text-[rgba(200,170,120,0.72)] hover:text-[#e8dcc8] transition-colors"
            aria-label={t('close')}
          >
            <X size={20} />
          </button>
        </div>

        <div className="p-6 flex flex-col gap-6">
          <div className="flex flex-col gap-4 p-4 border border-[rgba(200,148,55,0.14)] rounded-[4px] bg-gradient-to-b from-[rgba(200,148,55,0.04)] to-[rgba(200,148,55,0.015)] shadow-[inset_0_0_0_1px_rgba(255,200,100,0.03)]">
            <div className="flex justify-between items-center gap-4">
              <div className="flex flex-col gap-1.5">
                <p className="cinzel text-[10px] tracking-[0.18em] text-[rgba(216,188,123,0.8)] uppercase m-0">
                  {t('tutorialKicker')}
                </p>
                <p className="text-[13px] leading-relaxed text-[rgba(200,170,120,0.7)] m-0">
                  {t('installModalBody')}
                </p>
              </div>
              <a
                href="https://bazaarplusplus.com/tutorial"
                target="_blank"
                rel="noreferrer"
                className="shrink-0 flex items-center justify-center gap-2 px-3 py-2 border border-[rgba(214,169,84,0.24)] rounded-[3px] bg-gradient-to-b from-[rgba(200,148,55,0.12)] to-[rgba(200,148,55,0.06)] text-[rgba(236,225,202,0.88)] cinzel text-[10px] tracking-[0.12em] uppercase hover:border-[rgba(200,148,55,0.4)] transition-all no-underline"
              >
                {t('viewTutorial')}
                <ExternalLink size={12} />
              </a>
            </div>
          </div>

          <div className="flex items-start gap-3 p-4 border border-[rgba(232,190,120,0.24)] rounded-[4px] bg-[rgba(200,148,55,0.08)] text-[rgba(232,220,194,0.82)]">
            <AlertCircle
              size={16}
              className="mt-0.5 shrink-0 text-[rgba(232,190,120,0.9)]"
            />
            <p className="m-0 text-[13px] leading-relaxed">
              {t('installSteamNotice')}
            </p>
          </div>

          {showCompatToggle && (
            <label className="flex items-start gap-3 p-3 border border-[rgba(200,148,55,0.18)] rounded-[4px] bg-gradient-to-b from-[rgba(200,148,55,0.055)] to-[rgba(200,148,55,0.015)] group">
              <input
                type="checkbox"
                className="mt-1"
                checked={compat.forced ? true : compatOptIn}
                disabled={compat.forced}
                onChange={(event) => onCompatOptInChange(event.target.checked)}
              />
              <span className="flex flex-col gap-1 text-[13px] leading-relaxed text-[rgba(232,220,194,0.78)]">
                <span className="cinzel text-[rgba(232,200,130,0.9)]">
                  {t('compatModeLabel')}
                </span>
                <span className="text-[rgba(200,170,120,0.72)]">
                  {compat.forced
                    ? t('compatModeForcedNotice')
                    : t('compatModeDescription')}
                </span>
              </span>
            </label>
          )}

          <label className="flex items-start gap-3 p-3 border border-[rgba(200,148,55,0.18)] rounded-[4px] bg-gradient-to-b from-[rgba(200,148,55,0.055)] to-[rgba(200,148,55,0.015)] group">
            <input
              type="checkbox"
              className="mt-1"
              checked={installAcknowledged}
              onChange={(event) => onAcknowledgedChange(event.target.checked)}
            />
            <span className="text-[13px] leading-relaxed text-[rgba(232,220,194,0.78)]">
              {t('installAcknowledge')}
            </span>
          </label>

          <div className="flex justify-end gap-3 pt-2">
            <button
              type="button"
              onClick={onClose}
              className="px-5 py-2 bg-[rgba(200,148,55,0.04)] border border-[rgba(180,130,48,0.2)] rounded-sm hover:bg-[rgba(200,148,55,0.1)] transition-colors text-sm text-[#e8dcc8]"
            >
              {t('cancel')}
            </button>
            <button
              type="button"
              disabled={!installAcknowledged || page.action === 'install'}
              onClick={onConfirm}
              className="px-5 py-2 rounded-sm text-sm cinzel font-bold tracking-wider transition-all bg-gradient-to-b from-[#d4a040] to-[#9e5c1e] text-[#0b0906] shadow-[0_0_15px_rgba(212,160,64,0.4)] hover:brightness-110 active:brightness-95 disabled:opacity-45 disabled:hover:brightness-100"
            >
              {page.action === 'install'
                ? t('installing')
                : t('confirmInstall')}
            </button>
          </div>
        </div>
      </div>
    </Dialog>
  );
}
