import { NavLink } from 'react-router-dom';
import type { ReactNode } from 'react';
import { Download, History, Info, MonitorPlay } from 'lucide-react';
import clsx from 'clsx';
import { useI18n } from '../i18n/LocaleProvider';

export function ShellNavRail() {
  const { t } = useI18n();
  return (
    <nav className="flex-none w-48 border-r border-[rgba(200,148,55,0.18)] bg-[#0b0906] flex flex-col py-6 z-0">
      <RailItem to="/" icon={<Download size={18} />} label={t('navInstall')} />
      <RailItem
        to="/history"
        icon={<History size={18} />}
        label={t('navHistory')}
      />
      <RailItem
        to="/stream"
        icon={<MonitorPlay size={18} />}
        label={t('navStream')}
      />
      <RailItem to="/about" icon={<Info size={18} />} label={t('navAbout')} />
    </nav>
  );
}

function RailItem({
  to,
  icon,
  label
}: {
  to: string;
  icon: ReactNode;
  label: string;
}) {
  return (
    <NavLink
      to={to}
      className={({ isActive }) =>
        clsx(
          'flex items-center gap-3 px-6 py-3 cinzel tracking-widest transition-colors',
          isActive
            ? 'bg-[rgba(200,148,55,0.1)] text-[#e8c87a] border-r-2 border-[#e8c87a]'
            : 'text-[rgba(228,216,191,0.6)] hover:bg-[rgba(200,148,55,0.05)] hover:text-[#e8dcc8] border-r-2 border-transparent'
        )
      }
    >
      {icon}
      <span className="text-sm mt-0.5">{label}</span>
    </NavLink>
  );
}
