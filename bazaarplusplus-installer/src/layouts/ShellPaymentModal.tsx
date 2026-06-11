import { X } from 'lucide-react';
import wechatPaySvg from '../../static/support/wechat-pay.svg';
import { Dialog } from '../components/ui/Dialog';
import { useI18n } from '../i18n/LocaleProvider';

export function ShellPaymentModal({ onClose }: { onClose: () => void }) {
  const { t } = useI18n();
  return (
    <Dialog onClose={onClose} labelledBy="payment-modal-title">
      <div className="bg-[#0b0906] border border-[rgba(200,148,55,0.18)] rounded-[4px] shadow-[0_24px_64px_rgba(0,0,0,0.5)] w-full max-w-md mx-4 relative">
        <div className="flex justify-between items-center px-5 py-4 border-b border-[rgba(200,148,55,0.15)] bg-[rgba(200,148,55,0.02)]">
          <div>
            <p className="cinzel text-[10px] tracking-[0.2em] text-[rgba(200,148,55,0.5)] uppercase m-0 mb-1">
              BazaarPlusPlus
            </p>
            <h2
              id="payment-modal-title"
              className="cinzel text-[1.1rem] text-[#e8dcc8] m-0"
            >
              {t('supportProject')}
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

        <div className="p-6 flex flex-col gap-[0.9rem] items-center text-center">
          <article
            className="relative p-3 rounded-[4px] border border-[rgba(200,148,55,0.16)] flex flex-col gap-[0.65rem] w-full max-w-[260px] shadow-[inset_0_0_0_1px_rgba(255,198,98,0.05),0_10px_32px_rgba(42,110,78,0.14)]"
            style={{
              background:
                'radial-gradient(circle at top, rgba(255,232,174,0.08), transparent 54%), linear-gradient(180deg, rgba(34,20,8,0.96), rgba(16,9,4,0.98))'
            }}
          >
            <div className="absolute inset-[0.45rem] border border-[rgba(255,220,155,0.05)] rounded-[2px] pointer-events-none" />

            <div className="aspect-square p-[0.8rem] rounded-[3px] bg-gradient-to-br from-[rgba(255,248,231,0.98)] to-[rgba(245,238,220,0.98)] shadow-[inset_0_0_0_1px_rgba(95,65,19,0.08),0_10px_24px_rgba(0,0,0,0.22)] relative overflow-hidden">
              <img
                src={wechatPaySvg}
                alt={t('wechatPay')}
                className="w-full h-full object-contain rounded-[2px]"
              />
            </div>

            <div className="flex flex-col gap-[0.18rem] z-10">
              <h3 className="m-0 cinzel text-[0.82rem] tracking-[0.04em] text-[rgba(238,220,182,0.94)]">
                {t('wechatPay')}
              </h3>
              <p className="m-0 text-[0.66rem] leading-[1.45] text-[rgba(200,170,120,0.8)]">
                {t('wechatPayTagline')}
              </p>
            </div>
          </article>

          <div className="flex flex-col gap-1 mt-2">
            <p className="m-0 text-[0.76rem] leading-[1.6] text-[rgba(214,190,146,0.76)]">
              {t('supportLine1')}
            </p>
            <p className="m-0 text-[0.72rem] leading-[1.65] text-[rgba(240,220,184,0.82)] max-w-[28rem]">
              {t('supportLine2')}
            </p>
          </div>
        </div>
      </div>
    </Dialog>
  );
}
