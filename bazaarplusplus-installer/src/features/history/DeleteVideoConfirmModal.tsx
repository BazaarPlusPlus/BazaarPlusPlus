import { AlertTriangle, Loader2, X } from 'lucide-react';
import { Dialog } from '../../components/ui/Dialog';
import { useI18n } from '../../i18n/LocaleProvider';

export function DeleteVideoConfirmModal({
  busy,
  onClose,
  onConfirm
}: {
  busy: boolean;
  onClose: () => void;
  onConfirm: () => void | Promise<void>;
}) {
  const { t } = useI18n();

  return (
    <Dialog onClose={onClose} labelledBy="delete-video-modal-title">
      <div className="bg-[#0b0906] border border-[rgba(190,80,80,0.24)] rounded-[4px] shadow-[0_24px_64px_rgba(0,0,0,0.5)] w-full max-w-md mx-4 relative">
        <div className="flex justify-between items-center px-5 py-4 border-b border-[rgba(190,80,80,0.18)] bg-[rgba(160,50,50,0.06)]">
          <div className="flex items-center gap-3">
            <AlertTriangle size={18} className="text-[rgba(232,120,120,0.9)]" />
            <h2
              id="delete-video-modal-title"
              className="cinzel text-[1.1rem] text-[#f0d8d8] m-0 tracking-wider"
            >
              {t('deleteVideoConfirmTitle')}
            </h2>
          </div>
          <button
            type="button"
            onClick={onClose}
            className="text-[rgba(232,190,190,0.72)] hover:text-[#f0d8d8] transition-colors"
            aria-label={t('close')}
          >
            <X size={20} />
          </button>
        </div>

        <div className="p-6 flex flex-col gap-5">
          <p className="m-0 text-[13px] leading-relaxed text-[rgba(245,220,220,0.86)]">
            {t('deleteVideoConfirmBody')}
          </p>

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
              disabled={busy}
              onClick={onConfirm}
              className="px-5 py-2 rounded-sm text-sm cinzel font-bold tracking-wider transition-all bg-gradient-to-b from-[#d85d5d] to-[#9a2a2a] text-[#fff1f1] shadow-[0_0_15px_rgba(160,50,50,0.35)] hover:brightness-110 active:brightness-95 disabled:opacity-45 disabled:hover:brightness-100"
            >
              {busy ? (
                <span className="inline-flex items-center gap-2">
                  <Loader2 size={14} className="animate-spin" />
                  {t('deleteVideoConfirmAction')}
                </span>
              ) : (
                t('deleteVideoConfirmAction')
              )}
            </button>
          </div>
        </div>
      </div>
    </Dialog>
  );
}
