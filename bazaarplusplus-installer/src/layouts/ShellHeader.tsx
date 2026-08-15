import {
  Coffee,
  Copy,
  Heart,
  Languages,
  Minus,
  MonitorPlay,
  QrCode,
  Square,
  Users,
  X
} from 'lucide-react';
import { getCurrentWindow } from '@tauri-apps/api/window';
import {
  useEffect,
  useState,
  type CSSProperties,
  type ReactNode,
  type RefObject
} from 'react';
import { hasTauriRuntime } from '../api/runtime';
import type { AppBootstrapController } from '../features/about/useAppBootstrap';
import { isWindowsPlatform } from '../features/shared/platform';
import { useShellStreamServiceRunning } from '../features/stream/useShellStreamServiceRunning';
import { useI18n } from '../i18n/LocaleProvider';
import douyinPng from '../../static/support/douyin.png';
import xiaohongshuSvg from '../../static/support/xiaohongshu.svg';
import brandLogo from '../../static/brand/bazaarplusplus-logo.webp';

type ShellHeaderProps = {
  app: AppBootstrapController;
  bilibiliTriggerRef?: RefObject<HTMLButtonElement | null>;
  showBilibili: boolean;
  onToggleBilibili: () => void;
  showSupport: boolean;
  supportTriggerRef?: RefObject<HTMLButtonElement | null>;
  onToggleSupport: () => void;
  onOpenPayment: () => void;
  onCloseBilibili: () => void;
  onCloseSupport: () => void;
};

export function ShellHeader({
  app,
  bilibiliTriggerRef,
  showBilibili,
  onToggleBilibili,
  showSupport,
  supportTriggerRef,
  onToggleSupport,
  onOpenPayment,
  onCloseBilibili,
  onCloseSupport
}: ShellHeaderProps) {
  const { bootstrap } = app;

  return (
    <header className="bpp-header" data-tauri-drag-region>
      <ShellBrand />
      <ShellHeaderActions
        bootstrap={bootstrap}
        bilibiliTriggerRef={bilibiliTriggerRef}
        showBilibili={showBilibili}
        onToggleBilibili={onToggleBilibili}
        showSupport={showSupport}
        supportTriggerRef={supportTriggerRef}
        onToggleSupport={onToggleSupport}
        onOpenPayment={onOpenPayment}
        onCloseBilibili={onCloseBilibili}
        onCloseSupport={onCloseSupport}
      />
    </header>
  );
}

function ShellBrand() {
  const app = useAppBootstrapVersion();
  return (
    <div
      className="flex min-w-0 items-center gap-3 z-10"
      data-tauri-drag-region
    >
      <img
        src={brandLogo}
        alt=""
        className="bpp-brand-logo"
        draggable={false}
      />
      <h1 className="bpp-brand-title">BazaarPlusPlus</h1>
      <span className="bpp-version-chip">v{app}</span>
    </div>
  );
}

function useAppBootstrapVersion() {
  return __FRONTEND_VERSION__;
}

type ShellHeaderActionsProps = {
  bootstrap: AppBootstrapController['bootstrap'];
  bilibiliTriggerRef?: RefObject<HTMLButtonElement | null>;
  showBilibili: boolean;
  onToggleBilibili: () => void;
  showSupport: boolean;
  supportTriggerRef?: RefObject<HTMLButtonElement | null>;
  onToggleSupport: () => void;
  onOpenPayment: () => void;
  onCloseBilibili: () => void;
  onCloseSupport: () => void;
};

function ShellHeaderActions({
  bootstrap,
  bilibiliTriggerRef,
  showBilibili,
  onToggleBilibili,
  showSupport,
  supportTriggerRef,
  onToggleSupport,
  onOpenPayment,
  onCloseBilibili,
  onCloseSupport
}: ShellHeaderActionsProps) {
  const { t, toggle } = useI18n();

  return (
    <div className="bpp-header-actions">
      <div
        className="bpp-header-community-actions"
        data-header-group="community"
      >
        <ShellSocialLinks
          bootstrap={bootstrap}
          triggerRef={bilibiliTriggerRef}
          showBilibili={showBilibili}
          onToggleBilibili={onToggleBilibili}
          onCloseBilibili={onCloseBilibili}
        />
      </div>

      <span className="bpp-header-actions-divider" aria-hidden="true" />

      <div
        className="bpp-header-application-actions"
        data-header-group="application"
      >
        <ShellSupportMenu
          bootstrap={bootstrap}
          triggerRef={supportTriggerRef}
          showSupport={showSupport}
          onToggleSupport={onToggleSupport}
          onOpenPayment={onOpenPayment}
          onCloseSupport={onCloseSupport}
        />

        <button
          type="button"
          onClick={toggle}
          className="bpp-button bpp-language-button size-9"
          title={t('languageToggle')}
          aria-label={t('languageToggle')}
        >
          <Languages size={17} strokeWidth={1.8} aria-hidden="true" />
        </button>

        <WindowsWindowControls />
      </div>
    </div>
  );
}

function isWindowsTauriRuntime() {
  return hasTauriRuntime() && isWindowsPlatform();
}

function WindowsWindowControls() {
  const { t } = useI18n();
  const streamRunning = useShellStreamServiceRunning();
  const [isWindowsRuntime] = useState(isWindowsTauriRuntime);
  const [isMaximized, setIsMaximized] = useState(false);
  const closeLabel = streamRunning
    ? t('hideToTrayWhileStreaming')
    : t('closeWindow');
  const maximizeLabel = isMaximized ? t('restoreWindow') : t('maximizeWindow');

  useEffect(() => {
    if (!isWindowsRuntime) return;

    let active = true;
    let unlisten: (() => void) | undefined;
    const window = getCurrentWindow();
    const updateMaximized = async () => {
      try {
        const maximized = await window.isMaximized();
        if (active) setIsMaximized(maximized);
      } catch (error) {
        console.error(
          'Failed to read the Windows window maximized state.',
          error
        );
      }
    };

    void updateMaximized();
    void window
      .onResized(() => {
        void updateMaximized();
      })
      .then((stopListening) => {
        if (active) {
          unlisten = stopListening;
        } else {
          stopListening();
        }
      })
      .catch((error) => {
        console.error(
          'Failed to listen for Windows window resize events.',
          error
        );
      });

    return () => {
      active = false;
      unlisten?.();
    };
  }, [isWindowsRuntime]);

  if (!isWindowsRuntime) return null;

  const minimize = () => {
    void getCurrentWindow()
      .minimize()
      .catch((error) => {
        console.error('Failed to minimize the Windows window.', error);
      });
  };
  const toggleMaximize = () => {
    void getCurrentWindow()
      .toggleMaximize()
      .catch((error) => {
        console.error('Failed to toggle the Windows window size.', error);
      });
  };
  const close = () => {
    void getCurrentWindow()
      .close()
      .catch((error) => {
        console.error('Failed to close the Windows window.', error);
      });
  };

  return (
    <div className="bpp-window-controls" aria-label={t('windowControls')}>
      <button
        type="button"
        onClick={minimize}
        className="bpp-button bpp-window-control-button size-9 shrink-0"
        title={t('minimizeWindow')}
        aria-label={t('minimizeWindow')}
      >
        <Minus size={17} strokeWidth={1.8} aria-hidden="true" />
      </button>
      <button
        type="button"
        onClick={toggleMaximize}
        className="bpp-button bpp-window-control-button size-9 shrink-0"
        title={maximizeLabel}
        aria-label={maximizeLabel}
      >
        {isMaximized ? (
          <Copy size={15} strokeWidth={1.8} aria-hidden="true" />
        ) : (
          <Square size={15} strokeWidth={1.8} aria-hidden="true" />
        )}
      </button>
      <button
        type="button"
        onClick={close}
        className="bpp-button bpp-window-control-button bpp-window-close-button size-9 shrink-0"
        title={closeLabel}
        aria-label={closeLabel}
      >
        <X size={16} strokeWidth={1.8} aria-hidden="true" />
      </button>
    </div>
  );
}

type QrSocialEntryProps = {
  href?: string;
  accent: string;
  badge: string;
  label: string;
  qrAlt: string;
  qrSrc: string;
  qrRound?: boolean;
  subtitle: string;
  title: string;
  children: ReactNode;
};

function QrSocialEntry({
  href,
  accent,
  badge,
  label,
  qrAlt,
  qrSrc,
  qrRound = false,
  subtitle,
  title,
  children
}: QrSocialEntryProps) {
  const triggerClassName = 'bpp-header-icon-control';

  const trigger = href ? (
    <a
      href={href}
      target="_blank"
      rel="noopener noreferrer"
      className={triggerClassName}
      aria-label={label}
    >
      {children}
    </a>
  ) : (
    <button type="button" className={triggerClassName} aria-label={label}>
      {children}
    </button>
  );

  return (
    <div
      className="relative group flex"
      style={{ '--social-accent': accent } as CSSProperties}
    >
      {trigger}
      <div className="bpp-header-popover bpp-qr-popover">
        <div
          className="bpp-qr-badge"
          style={{
            borderColor: `${accent}80`,
            color: accent,
            backgroundColor: `${accent}10`
          }}
        >
          {badge}
        </div>
        <div className={`bpp-qr-image-frame ${qrRound ? 'is-round' : ''}`}>
          <img
            src={qrSrc}
            alt={qrAlt}
            loading="lazy"
            decoding="async"
            className="w-full h-full object-contain"
          />
        </div>
        <div className="bpp-qr-copy">
          <h3 className="bpp-qr-title">{title}</h3>
          <p className="bpp-qr-subtitle">{subtitle}</p>
        </div>
      </div>
    </div>
  );
}

function ShellSocialLinks({
  bootstrap,
  triggerRef,
  showBilibili,
  onToggleBilibili,
  onCloseBilibili
}: {
  bootstrap: AppBootstrapController['bootstrap'];
  triggerRef?: RefObject<HTMLButtonElement | null>;
  showBilibili: boolean;
  onToggleBilibili: () => void;
  onCloseBilibili: () => void;
}) {
  const { t } = useI18n();
  return (
    <div className="bpp-header-socials">
      <a
        href={bootstrap.links.github}
        target="_blank"
        rel="noopener noreferrer"
        className="bpp-header-icon-control"
        aria-label="GitHub"
      >
        <svg
          xmlns="http://www.w3.org/2000/svg"
          width="18"
          height="18"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          strokeWidth="2"
          strokeLinecap="round"
          strokeLinejoin="round"
        >
          <path d="M15 22v-4a4.8 4.8 0 0 0-1-3.5c3 0 6-2 6-5.5.08-1.25-.27-2.48-1-3.5.28-1.15.28-2.35 0-3.5 0 0-1 0-3 1.5-2.64-.5-5.36-.5-8 0C6 2 5 2 5 2c-.3 1.15-.3 2.35 0 3.5A5.403 5.403 0 0 0 4 9c0 3.5 3 5.5 6 5.5-.39.49-.68 1.05-.85 1.65-.17.6-.22 1.23-.15 1.85v4" />
          <path d="M9 18c-4.51 2-5-2-7-2" />
        </svg>
      </a>
      <a
        href={bootstrap.links.x}
        target="_blank"
        rel="noopener noreferrer"
        className="bpp-header-icon-control"
        aria-label="X"
      >
        <svg
          xmlns="http://www.w3.org/2000/svg"
          width="16"
          height="16"
          viewBox="0 0 24 24"
          fill="currentColor"
          aria-hidden="true"
        >
          <path d="M18.244 2.25h3.308l-7.227 8.26 8.502 11.24h-6.66l-5.214-6.817L4.99 21.75H1.68l7.73-8.835L1.254 2.25H8.08l4.713 6.231zm-1.161 17.52h1.833L7.084 4.126H5.117z" />
        </svg>
      </a>
      <QrSocialEntry
        href={bootstrap.links.xiaohongshu}
        accent="#ff2442"
        badge="REDNOTE"
        label={t('socialXiaohongshu')}
        qrAlt={t('socialXiaohongshuTitle')}
        qrSrc={xiaohongshuSvg}
        title={t('socialXiaohongshuTitle')}
        subtitle={t('socialXiaohongshuSubtitle')}
      >
        <svg
          xmlns="http://www.w3.org/2000/svg"
          width="18"
          height="18"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          strokeWidth="2"
          strokeLinecap="round"
          strokeLinejoin="round"
        >
          <path d="M4 19.5v-15A2.5 2.5 0 0 1 6.5 2H20v20H6.5a2.5 2.5 0 0 1 0-5H20" />
          <path d="M8 11h8" />
          <path d="M8 7h8" />
        </svg>
      </QrSocialEntry>

      <QrSocialEntry
        accent="#d4a040"
        badge="DOUYIN"
        label={t('socialDouyin')}
        qrAlt={t('socialDouyinTitle')}
        qrSrc={douyinPng}
        qrRound
        title={t('socialDouyinTitle')}
        subtitle={t('socialDouyinSubtitle')}
      >
        <svg
          xmlns="http://www.w3.org/2000/svg"
          width="18"
          height="18"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          strokeWidth="2"
          strokeLinecap="round"
          strokeLinejoin="round"
        >
          <path d="M14 3v11.5a4.5 4.5 0 1 1-4.5-4.5" />
          <path d="M14 6c1.35 2.25 3.1 3.5 5 3.8" />
        </svg>
      </QrSocialEntry>
      <div className="relative" data-dropdown>
        <button
          ref={triggerRef}
          type="button"
          onClick={onToggleBilibili}
          className="bpp-button bpp-header-labelled-control"
          aria-label={t('socialBilibili')}
          aria-expanded={showBilibili}
          aria-controls="shell-bilibili-menu"
          aria-haspopup="menu"
        >
          <svg
            xmlns="http://www.w3.org/2000/svg"
            width="15"
            height="15"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth="2"
            strokeLinecap="round"
            strokeLinejoin="round"
          >
            <rect width="18" height="12" x="3" y="8" rx="3" />
            <path d="m8 4 3 4" />
            <path d="m16 4-3 4" />
            <line x1="9" y1="13" x2="9.01" y2="13" />
            <line x1="15" y1="13" x2="15.01" y2="13" />
          </svg>
          <span className="whitespace-nowrap">{t('socialBilibili')}</span>
        </button>
        {showBilibili && (
          <div
            id="shell-bilibili-menu"
            role="menu"
            className="bpp-header-popover bpp-header-menu bpp-header-menu-centered"
          >
            <a
              role="menuitem"
              href={bootstrap.links.bilibili_author}
              target="_blank"
              rel="noopener noreferrer"
              className="bpp-header-menu-item group"
              onClick={onCloseBilibili}
            >
              <div className="bpp-header-menu-icon">
                <Users size={16} />
              </div>
              <div className="flex flex-col">
                <span className="bpp-header-menu-title">仓鼠小猫</span>
                <span className="bpp-header-menu-subtitle">
                  {t('bilibiliAuthorSubtitle')}
                </span>
              </div>
            </a>

            <div className="bpp-header-menu-divider" />

            <a
              role="menuitem"
              href={bootstrap.links.bilibili_core_dev}
              target="_blank"
              rel="noopener noreferrer"
              className="bpp-header-menu-item group"
              onClick={onCloseBilibili}
            >
              <div className="bpp-header-menu-icon">
                <Users size={16} />
              </div>
              <div className="flex flex-col">
                <span className="bpp-header-menu-title">hisenser</span>
                <span className="bpp-header-menu-subtitle">
                  {t('bilibiliCoreDevSubtitle')}
                </span>
              </div>
            </a>

            <div className="bpp-header-menu-divider" />

            <a
              role="menuitem"
              href={bootstrap.links.bilibili_project}
              target="_blank"
              rel="noopener noreferrer"
              className="bpp-header-menu-item group"
              onClick={onCloseBilibili}
            >
              <div className="bpp-header-menu-icon">
                <MonitorPlay size={16} />
              </div>
              <div className="flex flex-col">
                <span className="bpp-header-menu-title">BazaarPlusPlus</span>
                <span className="bpp-header-menu-subtitle">
                  {t('bilibiliProjectSubtitle')}
                </span>
              </div>
            </a>
          </div>
        )}
      </div>
    </div>
  );
}

function ShellSupportMenu({
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
        className="bpp-button bpp-header-labelled-control"
        onClick={onToggleSupport}
        aria-expanded={showSupport}
        aria-controls="shell-support-menu"
        aria-haspopup="menu"
      >
        <Heart size={14} />
        <span>{t('supportProject')}</span>
      </button>
      {showSupport && (
        <div
          id="shell-support-menu"
          role="menu"
          className="bpp-header-popover bpp-header-menu bpp-header-menu-right"
        >
          <button
            role="menuitem"
            type="button"
            className="bpp-header-menu-item group"
            onClick={onOpenPayment}
          >
            <div className="bpp-header-menu-icon">
              <QrCode size={16} />
            </div>
            <div className="flex flex-col">
              <span className="bpp-header-menu-title">{t('wechatPay')}</span>
              <span className="bpp-header-menu-subtitle">
                {t('wechatPayOpen')}
              </span>
            </div>
          </button>

          <div className="bpp-header-menu-divider" />

          <a
            role="menuitem"
            href={bootstrap.links.kofi}
            target="_blank"
            rel="noopener noreferrer"
            className="bpp-header-menu-item group"
            onClick={onCloseSupport}
          >
            <div className="bpp-header-menu-icon">
              <Coffee size={16} />
            </div>
            <div className="flex flex-col">
              <span className="bpp-header-menu-title">Ko-fi</span>
              <span className="bpp-header-menu-subtitle">
                {t('kofiSubtitle')}
              </span>
            </div>
          </a>

          <div className="bpp-header-menu-divider" />

          <a
            role="menuitem"
            href={bootstrap.links.supporter_list}
            target="_blank"
            rel="noopener noreferrer"
            className="bpp-header-menu-item group"
            onClick={onCloseSupport}
          >
            <div className="bpp-header-menu-icon">
              <Users size={16} />
            </div>
            <div className="flex flex-col">
              <span className="bpp-header-menu-title">
                {t('supporterList')}
              </span>
              <span className="bpp-header-menu-subtitle">
                {t('supporterListSubtitle')}
              </span>
            </div>
          </a>
        </div>
      )}
    </div>
  );
}
