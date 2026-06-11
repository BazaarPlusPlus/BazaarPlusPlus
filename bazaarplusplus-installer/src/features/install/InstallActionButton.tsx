import type { ReactNode } from 'react';
import { Loader2 } from 'lucide-react';

export function InstallActionButton({
  icon,
  label,
  onClick,
  disabled = false,
  busy = false,
  danger = false,
  className = ''
}: {
  icon: ReactNode;
  label: string;
  onClick: () => void;
  disabled?: boolean;
  busy?: boolean;
  danger?: boolean;
  className?: string;
}) {
  return (
    <button
      type="button"
      disabled={disabled}
      onClick={onClick}
      className={`min-h-9 px-3 py-2 text-xs leading-tight border rounded-sm transition-colors flex items-center justify-center gap-2 text-center disabled:opacity-40 ${
        danger
          ? 'bg-[rgba(160,50,50,0.08)] border-[rgba(190,80,80,0.2)] hover:bg-[rgba(160,50,50,0.14)] text-[rgba(232,190,190,0.9)]'
          : 'bg-[rgba(200,148,55,0.04)] border-[rgba(180,130,48,0.2)] hover:bg-[rgba(200,148,55,0.1)] text-[#e8dcc8]'
      } ${className}`}
    >
      {busy ? <Loader2 size={14} className="animate-spin" /> : icon}
      {label}
    </button>
  );
}
