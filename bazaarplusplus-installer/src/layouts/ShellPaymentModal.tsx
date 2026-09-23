import wechatPaySvg from '../../static/support/wechat-pay.svg';
import {
  Dialog,
  DialogBody,
  DialogCard,
  DialogHeader
} from '../components/ui/Dialog';
import { useI18n } from '../i18n/LocaleProvider';

export function ShellPaymentModal({ onClose }: { onClose: () => void }) {
  const { t } = useI18n();
  return (
    <Dialog onClose={onClose} labelledBy="payment-modal-title">
      <DialogCard size="sm">
        <DialogHeader
          titleId="payment-modal-title"
          title={t('supportProject')}
          onClose={onClose}
          closeLabel={t('close')}
        />
        <DialogBody className="items-center text-center">
          <img
            src={wechatPaySvg}
            alt={t('wechatPay')}
            className="bpp-qr-image object-contain"
          />
          <div className="flex flex-col gap-0.5">
            <h3 className="m-0 text-[13px] font-semibold text-fg-1">
              {t('wechatPay')}
            </h3>
            <p className="text-xs">{t('wechatPayTagline')}</p>
          </div>
          <div className="flex flex-col gap-1">
            <p className="text-fg-1">{t('supportLine1')}</p>
            <p className="text-xs">{t('supportLine2')}</p>
          </div>
        </DialogBody>
      </DialogCard>
    </Dialog>
  );
}
