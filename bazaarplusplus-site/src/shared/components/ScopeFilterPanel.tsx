import type { ReactNode } from 'react';

export function SegmentedControl({ children }: { children: ReactNode }) {
  return (
    <div className="inline-flex items-center rounded-full border border-[color:var(--color-border-soft)] bg-[color:rgba(10,8,5,0.7)] p-[3px] shadow-[inset_0_1px_0_rgba(255,235,200,0.04)]">
      {children}
    </div>
  );
}

export function SegmentedButton({
  active,
  onClick,
  onPreview,
  children,
}: {
  active: boolean;
  onClick: () => void;
  onPreview?: () => void;
  children: ReactNode;
}) {
  return (
    <button
      type="button"
      aria-pressed={active}
      onClick={onClick}
      onFocus={onPreview}
      onMouseEnter={onPreview}
      className={`relative shrink-0 rounded-full px-3.5 py-1.5 text-[0.78rem] font-semibold uppercase tracking-[0.12em] transition ${
        active
          ? 'bg-[color:var(--color-accent)] text-[color:#100c06] shadow-[0_4px_12px_rgba(232,185,74,0.32)]'
          : 'text-[color:var(--color-text-muted)] hover:text-[color:var(--color-text-base)]'
      }`}
    >
      {children}
    </button>
  );
}
