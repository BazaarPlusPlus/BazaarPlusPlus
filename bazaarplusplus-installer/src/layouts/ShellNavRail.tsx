import { NavLink, useLocation, useNavigate } from 'react-router-dom';
import type { CSSProperties, MouseEvent, ReactNode } from 'react';
import { Download, History, Info, MonitorPlay } from 'lucide-react';
import clsx from 'clsx';
import { useI18n } from '../i18n/LocaleProvider';
import { messages } from '../i18n/messages';

export function ShellNavRail() {
  const { locale, t } = useI18n();
  const { pathname } = useLocation();
  const activeIndex = pathname.startsWith('/history')
    ? 1
    : pathname.startsWith('/stream')
      ? 2
      : pathname.startsWith('/about')
        ? 3
        : 0;

  return (
    <nav className="bpp-nav" aria-label={t('primaryNavigation')}>
      <div
        className="bpp-nav-active-slider"
        aria-hidden="true"
        style={{ '--bpp-nav-index': activeIndex } as CSSProperties}
      />
      <RailItem
        to="/"
        index={0}
        activeIndex={activeIndex}
        icon={<Download size={21} />}
        label={t('navInstall')}
        secondary={locale === 'zh' ? 'INSTALL' : undefined}
      />
      <RailItem
        to="/history"
        index={1}
        activeIndex={activeIndex}
        icon={<History size={21} />}
        label={t('navHistory')}
        secondary={locale === 'zh' ? 'HISTORY' : undefined}
      />
      <RailItem
        to="/stream"
        index={2}
        activeIndex={activeIndex}
        icon={<MonitorPlay size={21} />}
        label={t('navStream')}
        secondary={locale === 'zh' ? 'STREAM' : undefined}
      />
      <RailItem
        to="/about"
        index={3}
        activeIndex={activeIndex}
        icon={<Info size={21} />}
        label={t('navAbout')}
        secondary={locale === 'zh' ? 'ABOUT' : undefined}
      />
      <div className="bpp-nav-footer">
        <span>◆</span> {t('kicker')}
        {locale === 'zh' && (
          <>
            <br />
            {messages.en.kicker}
          </>
        )}
      </div>
    </nav>
  );
}

function RailItem({
  to,
  index,
  activeIndex,
  icon,
  label,
  secondary
}: {
  to: string;
  index: number;
  activeIndex: number;
  icon: ReactNode;
  label: string;
  secondary?: string;
}) {
  const navigate = useNavigate();

  const navigateWithTransition = (event: MouseEvent<HTMLAnchorElement>) => {
    if (
      event.button !== 0 ||
      event.metaKey ||
      event.ctrlKey ||
      event.shiftKey ||
      event.altKey ||
      index === activeIndex
    ) {
      return;
    }

    event.preventDefault();
    const root = document.documentElement;
    if (root.dataset.bppNavTransition === 'running') return;

    const direction = index > activeIndex ? 'forward' : 'backward';
    const page = document.querySelector<HTMLElement>('.bpp-route-page');
    if (
      !page ||
      window.matchMedia('(prefers-reduced-motion: reduce)').matches
    ) {
      navigate(to);
      return;
    }

    root.dataset.bppNavTransition = 'running';
    page.classList.add(
      direction === 'forward' ? 'is-leaving-up' : 'is-leaving-down'
    );

    let completed = false;
    const finish = () => {
      if (completed) return;
      completed = true;
      window.clearTimeout(fallbackTimer);
      page.removeEventListener('animationend', onAnimationEnd);
      delete root.dataset.bppNavTransition;
      navigate(to);
    };
    const onAnimationEnd = (animationEvent: AnimationEvent) => {
      if (
        animationEvent.target === page &&
        animationEvent.animationName.startsWith('bpp-page-leave-')
      ) {
        finish();
      }
    };
    page.addEventListener('animationend', onAnimationEnd);
    const fallbackTimer = window.setTimeout(finish, 240);
  };

  return (
    <NavLink
      to={to}
      onClick={navigateWithTransition}
      className={({ isActive }) =>
        clsx('bpp-nav-item', isActive && 'is-active')
      }
    >
      <span className="bpp-nav-icon relative z-[2] flex items-center">
        {icon}
      </span>
      <span className="relative z-[2]">
        <span className="bpp-nav-primary">{label}</span>
        {secondary && <span className="bpp-nav-secondary">{secondary}</span>}
      </span>
    </NavLink>
  );
}
