import type { ReactNode } from 'react';

export function SegmentedControl({ children }: { children: ReactNode }) {
  return (
    <div className="inline-flex max-w-full items-center gap-0.5 overflow-x-auto rounded-control border border-line bg-canvas p-0.5">
      {children}
    </div>
  );
}

export function SegmentedButton({
  active,
  onClick,
  children,
}: {
  active: boolean;
  onClick: () => void;
  children: ReactNode;
}) {
  return (
    <button
      type="button"
      aria-pressed={active}
      onClick={onClick}
      className={`shrink-0 cursor-pointer rounded-[4px] px-2.5 py-1 text-xs font-medium whitespace-nowrap transition-colors duration-(--t-fast) focus-visible:outline-offset-0 ${
        active ? 'bg-selected text-text-1' : 'text-text-2 hover:text-text-1'
      }`}
    >
      {children}
    </button>
  );
}
