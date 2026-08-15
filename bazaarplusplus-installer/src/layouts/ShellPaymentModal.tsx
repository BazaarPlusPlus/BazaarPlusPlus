import { X } from 'lucide-react';
import wechatPaySvg from '../../static/support/wechat-pay.svg';
import { Dialog } from '../components/ui/Dialog';
import { useI18n } from '../i18n/LocaleProvider';

export function ShellPaymentModal({ onClose }: { onClose: () => void }) {
  const { t } = useI18n();
  return (
    <Dialog onClose={onClose} labelledBy="payment-modal-title">
      <div className="bpp-modal-card w-full max-w-md mx-4 relative">
        <div className="bpp-modal-header flex justify-between items-center px-5 py-4">
          <div>
            <p className="bpp-payment-kicker cinzel text-[10px] tracking-[0.2em] uppercase m-0 mb-1">
              BazaarPlusPlus
            </p>
            <h2
              id="payment-modal-title"
              className="bpp-payment-title cinzel text-[1.1rem] m-0"
            >
              {t('supportProject')}
            </h2>
          </div>
          <button
            type="button"
            onClick={onClose}
            className="bpp-payment-close transition-colors"
            aria-label={t('close')}
          >
            <X size={20} />
          </button>
        </div>

        <div className="p-6 flex flex-col gap-[0.9rem] items-center text-center">
          <article className="bpp-payment-card relative p-3 flex flex-col gap-[0.65rem] w-full max-w-[260px]">
            <div className="bpp-payment-card-inset absolute inset-[0.45rem] pointer-events-none" />

            <div className="bpp-payment-qr-frame aspect-square p-[0.8rem] relative overflow-hidden">
              <img
                src={wechatPaySvg}
                alt={t('wechatPay')}
                className="w-full h-full object-contain rounded-[2px]"
              />
            </div>

            <div className="flex flex-col gap-[0.18rem] z-10">
              <h3 className="bpp-payment-method m-0 cinzel text-[0.82rem] tracking-[0.04em]">
                {t('wechatPay')}
              </h3>
              <p className="bpp-payment-tagline m-0 leading-[1.45]">
                {t('wechatPayTagline')}
              </p>
            </div>
          </article>

          <div className="flex flex-col gap-1 mt-2">
            <p className="bpp-payment-support-copy m-0 leading-[1.6]">
              {t('supportLine1')}
            </p>
            <p className="bpp-payment-support-note m-0 leading-[1.65] max-w-[28rem]">
              {t('supportLine2')}
            </p>
          </div>
        </div>
      </div>
    </Dialog>
  );
}
