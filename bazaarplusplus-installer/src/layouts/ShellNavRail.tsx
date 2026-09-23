import { NavLink } from 'react-router-dom';
import type { ReactNode, RefObject } from 'react';
import { Download, History, Info, Languages, MonitorPlay } from 'lucide-react';
import clsx from 'clsx';
import type { AppBootstrapController } from '../features/about/useAppBootstrap';
import { useI18n } from '../i18n/LocaleProvider';
import { ShellSupportMenu } from './ShellSupportMenu';

/** Icon rail: the four pages, then support and language at its foot. Route
 *  changes are immediate; nothing waits on an animation before navigating. */
export function ShellNavRail({
  bootstrap,
  supportTriggerRef,
  showSupport,
  onToggleSupport,
  onOpenPayment,
  onCloseSupport
}: {
  bootstrap: AppBootstrapController['bootstrap'];
  supportTriggerRef?: RefObject<HTMLButtonElement | null>;
  showSupport: boolean;
  onToggleSupport: () => void;
  onOpenPayment: () => void;
  onCloseSupport: () => void;
}) {
  const { t, toggle } = useI18n();

  return (
    <nav className="bpp-nav" aria-label={t('primaryNavigation')}>
      <RailItem to="/" end icon={<Download />} label={t('navInstall')} />
      <RailItem to="/history" icon={<History />} label={t('navHistory')} />
      <RailItem to="/stream" icon={<MonitorPlay />} label={t('navStream')} />
      <RailItem to="/about" icon={<Info />} label={t('navAbout')} />
      <div className="bpp-nav-footer">
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
          className="bpp-rail-item"
          title={t('languageToggle')}
          aria-label={t('languageToggle')}
        >
          <Languages aria-hidden="true" />
          <span aria-hidden="true">{t('navLanguage')}</span>
        </button>
      </div>
    </nav>
  );
}

function RailItem({
  to,
  end = false,
  icon,
  label
}: {
  to: string;
  end?: boolean;
  icon: ReactNode;
  label: string;
}) {
  return (
    <NavLink
      to={to}
      end={end}
      className={({ isActive }) =>
        clsx('bpp-rail-item', isActive && 'is-active')
      }
    >
      <span className="flex" aria-hidden="true">
        {icon}
      </span>
      <span>{label}</span>
    </NavLink>
  );
}
