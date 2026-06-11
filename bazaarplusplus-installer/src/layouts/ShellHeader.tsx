import {
  AlertCircle,
  Check,
  Coffee,
  Download,
  Eye,
  Globe,
  Heart,
  MonitorPlay,
  QrCode,
  Users
} from 'lucide-react';
import type { CSSProperties, ReactNode } from 'react';
import type { AppBootstrapController } from '../features/about/useAppBootstrap';
import { useUpdater } from '../features/about/UpdaterProvider';
import { useI18n } from '../i18n/LocaleProvider';
import douyinPng from '../../static/support/douyin.png';
import xiaohongshuSvg from '../../static/support/xiaohongshu.svg';

type ShellHeaderProps = {
  app: AppBootstrapController;
  showBilibili: boolean;
  onToggleBilibili: () => void;
  showSupport: boolean;
  onToggleSupport: () => void;
  onOpenPayment: () => void;
  onCloseBilibili: () => void;
  onCloseSupport: () => void;
};

export function ShellHeader({
  app,
  showBilibili,
  onToggleBilibili,
  showSupport,
  onToggleSupport,
  onOpenPayment,
  onCloseBilibili,
  onCloseSupport
}: ShellHeaderProps) {
  const { bootstrap } = app;

  return (
    <header
      className="flex-none relative px-6 py-4 border-b border-[rgba(200,148,55,0.18)] z-20 flex flex-row items-center justify-between gap-4"
      style={{
        background:
          'linear-gradient(175deg, rgba(36,22,9,0.9), rgba(15,9,5,0.86))',
        boxShadow:
          '0 0 0 1px rgba(200,148,55,0.06) inset, 0 16px 42px rgba(0,0,0,0.42)'
      }}
    >
      <ShellHeaderCorners />
      <ShellBrand />
      <ShellHeaderActions
        bootstrap={bootstrap}
        showBilibili={showBilibili}
        onToggleBilibili={onToggleBilibili}
        showSupport={showSupport}
        onToggleSupport={onToggleSupport}
        onOpenPayment={onOpenPayment}
        onCloseBilibili={onCloseBilibili}
        onCloseSupport={onCloseSupport}
      />
    </header>
  );
}

function ShellHeaderCorners() {
  return (
    <>
      <div className="absolute top-2 left-2 text-[rgba(200,148,55,0.42)] pointer-events-none block">
        <svg width="28" height="28" viewBox="0 0 40 40" fill="none">
          <path
            d="M2 2L2 16M2 2L16 2"
            stroke="currentColor"
            strokeWidth="1.5"
            strokeLinecap="square"
          />
          <circle cx="2" cy="2" r="1.5" fill="currentColor" />
        </svg>
      </div>
      <div className="absolute top-2 right-2 text-[rgba(200,148,55,0.42)] pointer-events-none block">
        <svg width="28" height="28" viewBox="0 0 40 40" fill="none">
          <path
            d="M38 2L38 16M38 2L24 2"
            stroke="currentColor"
            strokeWidth="1.5"
            strokeLinecap="square"
          />
          <circle cx="38" cy="2" r="1.5" fill="currentColor" />
        </svg>
      </div>
    </>
  );
}

function ShellBrand() {
  const { t } = useI18n();
  return (
    <div className="flex items-center gap-3 z-10 ml-6">
      <div
        className="text-[rgba(205,150,60,0.65)] flex-shrink-0"
        style={{ filter: 'drop-shadow(0 0 7px rgba(205,150,60,0.22))' }}
      >
        <svg width="28" height="28" viewBox="0 0 44 44" fill="none">
          <polygon
            points="22,3 41,34 3,34"
            stroke="currentColor"
            strokeWidth="1"
            fill="none"
            opacity="0.55"
          />
          <polygon
            points="22,11 35,31 9,31"
            stroke="currentColor"
            strokeWidth="0.5"
            fill="none"
            opacity="0.3"
          />
          <circle
            cx="22"
            cy="22"
            r="5"
            stroke="currentColor"
            strokeWidth="0.8"
            fill="none"
          />
          <circle cx="22" cy="22" r="2" fill="currentColor" opacity="0.75" />
        </svg>
      </div>
      <div className="flex flex-row items-baseline gap-3">
        <h1
          className="cinzel-decorative text-2xl font-bold m-0 leading-none text-gold-text"
          style={{
            filter: 'drop-shadow(0 2px 10px rgba(205,150,60,0.28))'
          }}
        >
          BazaarPlusPlus
        </h1>
        <p className="m-0 italic text-[13px] text-[rgba(200,170,120,0.8)]">
          {t('kicker')}
        </p>
      </div>
    </div>
  );
}

type ShellHeaderActionsProps = {
  bootstrap: AppBootstrapController['bootstrap'];
  showBilibili: boolean;
  onToggleBilibili: () => void;
  showSupport: boolean;
  onToggleSupport: () => void;
  onOpenPayment: () => void;
  onCloseBilibili: () => void;
  onCloseSupport: () => void;
};

function ShellHeaderActions({
  bootstrap,
  showBilibili,
  onToggleBilibili,
  showSupport,
  onToggleSupport,
  onOpenPayment,
  onCloseBilibili,
  onCloseSupport
}: ShellHeaderActionsProps) {
  const { t, toggle } = useI18n();
  const updater = useUpdater();
  const checking = updater.phase === 'checking';

  // The check button folds its own result in: "检查中" → a brief result flash
  // ("已是最新" / "浏览器预览" / "检查失败"), then auto-reverts once useUpdater
  // clears the result phase on a timer. Install errors render in the modal.
  let checkIcon = Download;
  let checkLabel = t('headerCheckUpdate');
  let checkTitle: string | undefined;
  let checkErrorTone = false;
  if (checking) {
    checkLabel = t('headerCheckingUpdate');
  } else if (updater.phase === 'current') {
    checkIcon = Check;
    checkLabel = t('updaterCurrent');
  } else if (updater.phase === 'preview') {
    checkIcon = Eye;
    checkLabel = t('updaterPreview');
  } else if (updater.phase === 'error' && updater.errorSource === 'check') {
    checkIcon = AlertCircle;
    checkLabel = t('headerCheckFailed');
    checkTitle = updater.error ?? undefined;
    checkErrorTone = true;
  }
  const CheckIcon = checkIcon;

  return (
    <div className="flex items-center gap-3 z-10 justify-end mr-6">
      <ShellSocialLinks
        bootstrap={bootstrap}
        showBilibili={showBilibili}
        onToggleBilibili={onToggleBilibili}
        onCloseBilibili={onCloseBilibili}
      />

      <button
        type="button"
        onClick={updater.checkNow}
        disabled={checking}
        title={checkTitle}
        className="flex items-center gap-2 px-3 h-8 border border-[rgba(200,148,55,0.24)] rounded-[2px] cinzel text-[10px] tracking-widest uppercase transition-all hover:border-[rgba(200,148,55,0.4)] disabled:opacity-60"
        style={{
          background:
            'linear-gradient(180deg, rgba(200,148,55,0.12), rgba(200,148,55,0.06))',
          color: checkErrorTone
            ? 'rgba(224,150,130,0.92)'
            : 'rgba(228,216,191,0.82)',
          boxShadow: '0 0 0 1px rgba(255,198,98,0.08) inset'
        }}
      >
        <CheckIcon size={14} className={checking ? 'animate-pulse' : ''} />
        <span className="inline">{checkLabel}</span>
      </button>

      <ShellSupportMenu
        bootstrap={bootstrap}
        showSupport={showSupport}
        onToggleSupport={onToggleSupport}
        onOpenPayment={onOpenPayment}
        onCloseSupport={onCloseSupport}
      />

      <button
        type="button"
        onClick={toggle}
        className="flex items-center justify-center size-8 border border-[rgba(200,148,55,0.24)] rounded-[2px] transition-all hover:border-[rgba(200,148,55,0.4)]"
        style={{
          background:
            'linear-gradient(180deg, rgba(200,148,55,0.12), rgba(200,148,55,0.06))',
          color: 'rgba(228,216,191,0.82)',
          boxShadow: '0 0 0 1px rgba(255,198,98,0.08) inset'
        }}
        title={t('languageToggle')}
        aria-label={t('languageToggle')}
      >
        <Globe size={16} />
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
  const triggerClassName =
    'flex items-center justify-center size-8 text-[rgba(200,170,120,0.72)] hover:text-[var(--social-accent)] focus-visible:text-[var(--social-accent)] transition-colors';

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
      <div className="absolute top-[calc(100%+0.5rem)] left-1/2 w-[260px] bg-[#0b0906] border border-[rgba(200,148,55,0.2)] rounded-[4px] shadow-[0_16px_40px_rgba(0,0,0,0.6),inset_0_0_0_1px_rgba(255,198,98,0.05)] p-5 z-50 opacity-0 pointer-events-none group-hover:opacity-100 group-hover:pointer-events-auto group-focus-within:opacity-100 group-focus-within:pointer-events-auto transition-all duration-200 transform -translate-x-1/2 translate-y-2 group-hover:translate-y-0 group-focus-within:translate-y-0 flex flex-col items-center gap-4">
        <div
          className="border rounded-[2px] px-3 py-[0.15rem] text-[0.55rem] tracking-[0.15em] font-bold"
          style={{
            borderColor: `${accent}80`,
            color: accent,
            backgroundColor: `${accent}10`
          }}
        >
          {badge}
        </div>
        <div
          className={`w-full aspect-square bg-[#f8f0e3] ${qrRound ? 'rounded-full' : 'rounded-[2px]'} p-2 shadow-[inset_0_0_0_1px_rgba(212,160,64,0.4)] flex items-center justify-center`}
        >
          <img
            src={qrSrc}
            alt={qrAlt}
            loading="lazy"
            decoding="async"
            className="w-full h-full object-contain"
          />
        </div>
        <div className="flex flex-col items-center gap-[0.15rem]">
          <h3 className="font-bold text-[#d4a040] tracking-[0.08em] text-[1.05rem] m-0 leading-none">
            {title}
          </h3>
          <p className="text-[rgba(200,170,120,0.8)] text-[0.72rem] tracking-wide m-0">
            {subtitle}
          </p>
        </div>
      </div>
    </div>
  );
}

function ShellSocialLinks({
  bootstrap,
  showBilibili,
  onToggleBilibili,
  onCloseBilibili
}: {
  bootstrap: AppBootstrapController['bootstrap'];
  showBilibili: boolean;
  onToggleBilibili: () => void;
  onCloseBilibili: () => void;
}) {
  const { t } = useI18n();
  return (
    <div className="flex items-center gap-1 mr-2">
      <a
        href={bootstrap.links.github}
        target="_blank"
        rel="noopener noreferrer"
        className="flex items-center justify-center size-8 text-[rgba(200,170,120,0.72)] hover:text-[#e8c87a] transition-colors"
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
        className="flex items-center justify-center size-8 text-[rgba(200,170,120,0.72)] hover:text-[#e8c87a] transition-colors"
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
          type="button"
          onClick={onToggleBilibili}
          className="flex min-w-[66px] items-center justify-center gap-1.5 px-2.5 h-8 border border-[rgba(0,161,214,0.28)] rounded-[2px] text-[11px] font-medium tracking-[0.04em] transition-all hover:border-[rgba(0,161,214,0.55)] hover:text-[#7ad8ff]"
          style={{
            background:
              'linear-gradient(180deg, rgba(0,161,214,0.1), rgba(200,148,55,0.05))',
            color: 'rgba(228,216,191,0.82)',
            boxShadow: '0 0 0 1px rgba(255,198,98,0.06) inset'
          }}
          aria-label={t('socialBilibili')}
          aria-expanded={showBilibili}
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
          <div className="absolute top-[calc(100%+0.5rem)] left-1/2 w-[260px] bg-[rgba(18,11,5,0.95)] backdrop-blur-md border border-[rgba(200,148,55,0.2)] rounded-sm shadow-[0_16px_40px_rgba(0,0,0,0.6)] p-1.5 z-50 flex flex-col gap-1 transform -translate-x-1/2">
            <a
              href={bootstrap.links.bilibili_project}
              target="_blank"
              rel="noopener noreferrer"
              className="flex items-center gap-3 px-3 py-2.5 hover:bg-[rgba(200,148,55,0.1)] rounded-sm text-left transition-all group no-underline"
              onClick={onCloseBilibili}
            >
              <div className="flex items-center justify-center size-8 rounded-sm bg-[rgba(200,148,55,0.05)] border border-[rgba(200,148,55,0.1)] group-hover:border-[rgba(200,148,55,0.3)] group-hover:bg-[rgba(200,148,55,0.15)] transition-colors text-[rgba(200,170,120,0.8)] group-hover:text-[#e8c87a]">
                <MonitorPlay size={16} />
              </div>
              <div className="flex flex-col">
                <span className="text-sm font-medium text-[#e8dcc8] group-hover:text-[#f4ead5] transition-colors">
                  BazaarPlusPlus
                </span>
                <span className="text-[10px] text-[rgba(200,170,120,0.8)]">
                  {t('bilibiliProjectSubtitle')}
                </span>
              </div>
            </a>

            <div className="h-px bg-gradient-to-r from-transparent via-[rgba(200,148,55,0.2)] to-transparent my-0.5 mx-2" />

            <a
              href={bootstrap.links.bilibili_author}
              target="_blank"
              rel="noopener noreferrer"
              className="flex items-center gap-3 px-3 py-2.5 hover:bg-[rgba(200,148,55,0.1)] rounded-sm text-left transition-all group no-underline"
              onClick={onCloseBilibili}
            >
              <div className="flex items-center justify-center size-8 rounded-sm bg-[rgba(200,148,55,0.05)] border border-[rgba(200,148,55,0.1)] group-hover:border-[rgba(200,148,55,0.3)] group-hover:bg-[rgba(200,148,55,0.15)] transition-colors text-[rgba(200,170,120,0.8)] group-hover:text-[#e8c87a]">
                <Users size={16} />
              </div>
              <div className="flex flex-col">
                <span className="text-sm font-medium text-[#e8dcc8] group-hover:text-[#f4ead5] transition-colors">
                  小杨不努力
                </span>
                <span className="text-[10px] text-[rgba(200,170,120,0.8)]">
                  {t('bilibiliAuthorSubtitle')}
                </span>
              </div>
            </a>
          </div>
        )}
      </div>
      <div className="w-px h-4 bg-[rgba(200,148,55,0.2)] mx-1" />
    </div>
  );
}

function ShellSupportMenu({
  bootstrap,
  showSupport,
  onToggleSupport,
  onOpenPayment,
  onCloseSupport
}: {
  bootstrap: AppBootstrapController['bootstrap'];
  showSupport: boolean;
  onToggleSupport: () => void;
  onOpenPayment: () => void;
  onCloseSupport: () => void;
}) {
  const { t } = useI18n();
  return (
    <div className="relative" data-dropdown>
      <button
        type="button"
        className="flex items-center gap-2 px-3 h-8 border border-[rgba(200,148,55,0.24)] rounded-[2px] cinzel text-[10px] tracking-widest uppercase transition-all hover:border-[rgba(200,148,55,0.4)]"
        style={{
          background:
            'linear-gradient(180deg, rgba(200,148,55,0.12), rgba(200,148,55,0.06))',
          color: 'rgba(228,216,191,0.82)',
          boxShadow: '0 0 0 1px rgba(255,198,98,0.08) inset'
        }}
        onClick={onToggleSupport}
      >
        <Heart size={14} />
        <span>{t('supportProject')}</span>
      </button>
      {showSupport && (
        <div className="absolute top-[calc(100%+0.5rem)] right-0 w-56 bg-[rgba(18,11,5,0.95)] backdrop-blur-md border border-[rgba(200,148,55,0.2)] rounded-sm shadow-[0_16px_40px_rgba(0,0,0,0.6)] p-1.5 z-50 flex flex-col gap-1">
          <button
            type="button"
            className="flex items-center gap-3 px-3 py-2.5 hover:bg-[rgba(200,148,55,0.1)] rounded-sm text-left transition-all group"
            onClick={onOpenPayment}
          >
            <div className="flex items-center justify-center size-8 rounded-sm bg-[rgba(200,148,55,0.05)] border border-[rgba(200,148,55,0.1)] group-hover:border-[rgba(200,148,55,0.3)] group-hover:bg-[rgba(200,148,55,0.15)] transition-colors text-[rgba(200,170,120,0.8)] group-hover:text-[#e8c87a]">
              <QrCode size={16} />
            </div>
            <div className="flex flex-col">
              <span className="text-sm font-medium text-[#e8dcc8] group-hover:text-[#f4ead5] transition-colors">
                {t('wechatPay')}
              </span>
              <span className="text-[10px] text-[rgba(200,170,120,0.8)]">
                {t('wechatPayOpen')}
              </span>
            </div>
          </button>

          <div className="h-px bg-gradient-to-r from-transparent via-[rgba(200,148,55,0.2)] to-transparent my-0.5 mx-2" />

          <a
            href={bootstrap.links.kofi}
            target="_blank"
            rel="noopener noreferrer"
            className="flex items-center gap-3 px-3 py-2.5 hover:bg-[rgba(200,148,55,0.1)] rounded-sm text-left transition-all group no-underline"
            onClick={onCloseSupport}
          >
            <div className="flex items-center justify-center size-8 rounded-sm bg-[rgba(200,148,55,0.05)] border border-[rgba(200,148,55,0.1)] group-hover:border-[rgba(200,148,55,0.3)] group-hover:bg-[rgba(200,148,55,0.15)] transition-colors text-[rgba(200,170,120,0.8)] group-hover:text-[#e8c87a]">
              <Coffee size={16} />
            </div>
            <div className="flex flex-col">
              <span className="text-sm font-medium text-[#e8dcc8] group-hover:text-[#f4ead5] transition-colors">
                Ko-fi
              </span>
              <span className="text-[10px] text-[rgba(200,170,120,0.8)]">
                {t('kofiSubtitle')}
              </span>
            </div>
          </a>

          <div className="h-px bg-gradient-to-r from-transparent via-[rgba(200,148,55,0.2)] to-transparent my-0.5 mx-2" />

          <a
            href={bootstrap.links.supporter_list}
            target="_blank"
            rel="noopener noreferrer"
            className="flex items-center gap-3 px-3 py-2.5 hover:bg-[rgba(200,148,55,0.1)] rounded-sm text-left transition-all group no-underline"
            onClick={onCloseSupport}
          >
            <div className="flex items-center justify-center size-8 rounded-sm bg-[rgba(200,148,55,0.05)] border border-[rgba(200,148,55,0.1)] group-hover:border-[rgba(200,148,55,0.3)] group-hover:bg-[rgba(200,148,55,0.15)] transition-colors text-[rgba(200,170,120,0.8)] group-hover:text-[#e8c87a]">
              <Users size={16} />
            </div>
            <div className="flex flex-col">
              <span className="text-sm font-medium text-[#e8dcc8] group-hover:text-[#f4ead5] transition-colors">
                {t('supporterList')}
              </span>
              <span className="text-[10px] text-[rgba(200,170,120,0.8)]">
                {t('supporterListSubtitle')}
              </span>
            </div>
          </a>
        </div>
      )}
    </div>
  );
}
