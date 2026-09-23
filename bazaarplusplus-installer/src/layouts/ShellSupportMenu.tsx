import { Coffee, Heart, QrCode, Users } from 'lucide-react';
import type { RefObject } from 'react';
import type { AppBootstrapController } from '../features/about/useAppBootstrap';
import { useI18n } from '../i18n/LocaleProvider';

/** The rail's support entry: WeChat Pay, Ko-fi and the supporter list. The
 *  menu opens beside the rail, growing upward from its trigger. */
export function ShellSupportMenu({
  bootstrap,
  triggerRef,
  showSupport,
  onToggleSupport,
  onOpenPayment,
  onCloseSupport
}: {
  bootstrap: AppBootstrapController['bootstrap'];
  triggerRef?: RefObject<HTMLButtonElement | null>;
  showSupport: boolean;
  onToggleSupport: () => void;
  onOpenPayment: () => void;
  onCloseSupport: () => void;
}) {
  const { t } = useI18n();
  return (
    <div className="relative" data-dropdown>
      <button
        ref={triggerRef}
        type="button"
        className="bpp-rail-item"
        onClick={onToggleSupport}
        title={t('supportProject')}
        aria-expanded={showSupport}
        aria-controls="shell-support-menu"
        aria-haspopup="menu"
      >
        <Heart aria-hidden="true" />
        <span>{t('navSupport')}</span>
      </button>
      {showSupport && (
        <div
          id="shell-support-menu"
          role="menu"
          className="bpp-popover bpp-menu is-beside"
        >
          <button
            role="menuitem"
            type="button"
            className="bpp-menu-item"
            onClick={onOpenPayment}
          >
            <QrCode aria-hidden="true" />
            <div className="min-w-0">
              <span className="bpp-menu-title">{t('wechatPay')}</span>
              <span className="bpp-menu-subtitle">{t('wechatPayOpen')}</span>
            </div>
          </button>

          <a
            role="menuitem"
            href={bootstrap.links.kofi}
            target="_blank"
            rel="noopener noreferrer"
            className="bpp-menu-item"
            onClick={onCloseSupport}
          >
            <Coffee aria-hidden="true" />
            <div className="min-w-0">
              <span className="bpp-menu-title">Ko-fi</span>
              <span className="bpp-menu-subtitle">{t('kofiSubtitle')}</span>
            </div>
          </a>

          <a
            role="menuitem"
            href={bootstrap.links.supporter_list}
            target="_blank"
            rel="noopener noreferrer"
            className="bpp-menu-item"
            onClick={onCloseSupport}
          >
            <Users aria-hidden="true" />
            <div className="min-w-0">
              <span className="bpp-menu-title">{t('supporterList')}</span>
              <span className="bpp-menu-subtitle">
                {t('supporterListSubtitle')}
              </span>
            </div>
          </a>
        </div>
      )}
    </div>
  );
}
