import {
  Copy,
  Minus,
  MonitorPlay,
  RefreshCw,
  Square,
  Users,
  X
} from 'lucide-react';
import { getCurrentWindow } from '@tauri-apps/api/window';
import { useEffect, useState, type ReactNode, type RefObject } from 'react';
import { hasTauriRuntime } from '../api/runtime';
import { Button } from '../components/ui/Button';
import { useUpdater } from '../features/about/UpdaterProvider';
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
  onCloseBilibili: () => void;
};

export function ShellHeader({
  app,
  bilibiliTriggerRef,
  showBilibili,
  onToggleBilibili,
  onCloseBilibili
}: ShellHeaderProps) {
  const { bootstrap } = app;

  return (
    <header className="bpp-header" data-tauri-drag-region="deep">
      <MacFullscreenWatcher />
      <ShellBrand />
      <ShellHeaderActions
        bootstrap={bootstrap}
        bilibiliTriggerRef={bilibiliTriggerRef}
        showBilibili={showBilibili}
        onToggleBilibili={onToggleBilibili}
        onCloseBilibili={onCloseBilibili}
      />
    </header>
  );
}

function ShellBrand() {
  return (
    <div className="bpp-brand">
      <img
        src={brandLogo}
        alt=""
        className="bpp-brand-logo"
        draggable={false}
      />
      <h1 className="bpp-brand-title">BazaarPlusPlus</h1>
      <span className="bpp-brand-version">v{__FRONTEND_VERSION__}</span>
    </div>
  );
}

type ShellHeaderActionsProps = {
  bootstrap: AppBootstrapController['bootstrap'];
  bilibiliTriggerRef?: RefObject<HTMLButtonElement | null>;
  showBilibili: boolean;
  onToggleBilibili: () => void;
  onCloseBilibili: () => void;
};

function ShellHeaderActions({
  bootstrap,
  bilibiliTriggerRef,
  showBilibili,
  onToggleBilibili,
  onCloseBilibili
}: ShellHeaderActionsProps) {
  return (
    <div className="bpp-header-actions">
      <div className="bpp-header-group" data-header-group="community">
        <ShellSocialLinks
          bootstrap={bootstrap}
          triggerRef={bilibiliTriggerRef}
          showBilibili={showBilibili}
          onToggleBilibili={onToggleBilibili}
          onCloseBilibili={onCloseBilibili}
        />
      </div>

      <span className="bpp-header-actions-divider" aria-hidden="true" />
      <ShellUpdateCheck />

      <WindowsWindowControls />
    </div>
  );
}

/** App-wide update check; the updater opens its own dialog when a release is
 *  available and reports "already current" through a toast. */
function ShellUpdateCheck() {
  const { t } = useI18n();
  const updater = useUpdater();
  const checking = updater.phase === 'checking';
  return (
    <Button
      variant="ghost"
      size="sm"
      busy={checking}
      onClick={updater.checkNow}
      icon={<RefreshCw aria-hidden="true" />}
    >
      {t('headerCheckUpdate')}
    </Button>
  );
}

/**
 * The macOS overlay title bar hides its traffic lights in native fullscreen;
 * mirror that state so the header drops the inset reserved for them.
 */
function MacFullscreenWatcher() {
  const [overlayTitlebar] = useState(
    () => document.documentElement.dataset.bppTitlebar === 'overlay'
  );
  useEffect(() => {
    if (!overlayTitlebar) return;
    let active = true;
    let unlisten: (() => void) | undefined;
    const window = getCurrentWindow();
    const sync = async () => {
      try {
        const fullscreen = await window.isFullscreen();
        if (!active) return;
        if (fullscreen) document.documentElement.dataset.bppFullscreen = '';
        else delete document.documentElement.dataset.bppFullscreen;
      } catch (error) {
        console.error('Failed to read the macOS fullscreen state.', error);
      }
    };
    void sync();
    void window
      .onResized(() => void sync())
      .then((stop) => {
        if (active) unlisten = stop;
        else stop();
      })
      .catch((error) => {
        console.error('Failed to listen for macOS resize events.', error);
      });
    return () => {
      active = false;
      unlisten?.();
    };
  }, [overlayTitlebar]);
  return null;
}

function isWindowsTauriRuntime() {
  return hasTauriRuntime() && isWindowsPlatform();
}

/**
 * Gate, not a wrapper: the hooks that poll native state live in the inner
 * component so they are never scheduled off Windows, where the controls render
 * nothing at all.
 */
function WindowsWindowControls() {
  const [isWindowsRuntime] = useState(isWindowsTauriRuntime);
  if (!isWindowsRuntime) return null;
  return <WindowsWindowControlsContent />;
}

function minimizeWindow() {
  void getCurrentWindow()
    .minimize()
    .catch((error) => {
      console.error('Failed to minimize the Windows window.', error);
    });
}

function toggleMaximizeWindow() {
  void getCurrentWindow()
    .toggleMaximize()
    .catch((error) => {
      console.error('Failed to toggle the Windows window size.', error);
    });
}

function closeWindow() {
  void getCurrentWindow()
    .close()
    .catch((error) => {
      console.error('Failed to close the Windows window.', error);
    });
}

function WindowsWindowControlsContent() {
  const { t } = useI18n();
  const streamRunning = useShellStreamServiceRunning();
  const [isMaximized, setIsMaximized] = useState(false);
  const closeLabel = streamRunning
    ? t('hideToTrayWhileStreaming')
    : t('closeWindow');
  const maximizeLabel = isMaximized ? t('restoreWindow') : t('maximizeWindow');

  useEffect(() => {
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
  }, []);

  return (
    <div className="bpp-window-controls" aria-label={t('windowControls')}>
      <button
        type="button"
        onClick={minimizeWindow}
        className="bpp-window-control-button"
        title={t('minimizeWindow')}
        aria-label={t('minimizeWindow')}
      >
        <Minus size={16} strokeWidth={1.6} aria-hidden="true" />
      </button>
      <button
        type="button"
        onClick={toggleMaximizeWindow}
        className="bpp-window-control-button"
        title={maximizeLabel}
        aria-label={maximizeLabel}
      >
        {isMaximized ? (
          <Copy size={13} strokeWidth={1.6} aria-hidden="true" />
        ) : (
          <Square size={13} strokeWidth={1.6} aria-hidden="true" />
        )}
      </button>
      <button
        type="button"
        onClick={closeWindow}
        className="bpp-window-control-button bpp-window-close-button"
        title={closeLabel}
        aria-label={closeLabel}
      >
        <X size={16} strokeWidth={1.6} aria-hidden="true" />
      </button>
    </div>
  );
}

type QrSocialEntryProps = {
  href?: string;
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
    <div className="relative group flex">
      {trigger}
      <div
        className="bpp-popover bpp-qr-popover"
        data-tauri-drag-region="false"
      >
        <img
          src={qrSrc}
          alt={qrAlt}
          loading="lazy"
          decoding="async"
          className={`bpp-qr-image object-contain ${qrRound ? 'is-round' : ''}`}
        />
        <div>
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
    <>
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
          className="bpp-btn bpp-btn-ghost bpp-btn-sm"
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
            className="bpp-popover bpp-menu is-centered"
            data-tauri-drag-region="false"
          >
            <a
              role="menuitem"
              href={bootstrap.links.bilibili_core_dev}
              target="_blank"
              rel="noopener noreferrer"
              className="bpp-menu-item"
              onClick={onCloseBilibili}
            >
              <Users aria-hidden="true" />
              <div className="min-w-0">
                <span className="bpp-menu-title">hisenser</span>
                <span className="bpp-menu-subtitle">
                  {t('bilibiliCoreDevSubtitle')}
                </span>
              </div>
            </a>

            <a
              role="menuitem"
              href={bootstrap.links.bilibili_author}
              target="_blank"
              rel="noopener noreferrer"
              className="bpp-menu-item"
              onClick={onCloseBilibili}
            >
              <Users aria-hidden="true" />
              <div className="min-w-0">
                <span className="bpp-menu-title">仓鼠小猫</span>
                <span className="bpp-menu-subtitle">
                  {t('bilibiliAuthorSubtitle')}
                </span>
              </div>
            </a>

            <a
              role="menuitem"
              href={bootstrap.links.bilibili_project}
              target="_blank"
              rel="noopener noreferrer"
              className="bpp-menu-item"
              onClick={onCloseBilibili}
            >
              <MonitorPlay aria-hidden="true" />
              <div className="min-w-0">
                <span className="bpp-menu-title">BazaarPlusPlus</span>
                <span className="bpp-menu-subtitle">
                  {t('bilibiliProjectSubtitle')}
                </span>
              </div>
            </a>
          </div>
        )}
      </div>
    </>
  );
}
