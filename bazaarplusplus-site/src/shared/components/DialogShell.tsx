import { useEffect, useRef, type ReactNode } from 'react';

type DialogShellProps = {
  open: boolean;
  onClose: () => void;
  labelledBy: string;
  closeLabel: string;
  children: ReactNode;
};

export default function DialogShell({
  open,
  onClose,
  labelledBy,
  closeLabel,
  children,
}: DialogShellProps) {
  const cardRef = useRef<HTMLDivElement>(null);
  const previousFocusRef = useRef<HTMLElement | null>(null);

  useEffect(() => {
    if (!open) {
      return;
    }

    previousFocusRef.current =
      document.activeElement instanceof HTMLElement ? document.activeElement : null;
    cardRef.current?.focus();

    function handleKey(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        event.stopPropagation();
        onClose();
      }
    }

    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    document.addEventListener('keydown', handleKey);

    return () => {
      document.removeEventListener('keydown', handleKey);
      document.body.style.overflow = previousOverflow;
      previousFocusRef.current?.focus();
    };
  }, [open, onClose]);

  if (!open) {
    return null;
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center px-6 py-10">
      <button
        type="button"
        aria-label={closeLabel}
        onClick={onClose}
        className="absolute inset-0 cursor-default bg-[rgba(8,6,4,0.78)] backdrop-blur-sm"
      />
      <div
        ref={cardRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby={labelledBy}
        tabIndex={-1}
        className="relative w-full rounded-3xl border border-[color:var(--color-border-soft)] bg-[color:var(--color-bg-card)] p-8 shadow-[0_36px_80px_-20px_rgba(0,0,0,0.7)] outline-none max-w-md"
      >
        <button
          type="button"
          aria-label={closeLabel}
          onClick={onClose}
          className="absolute right-4 top-4 inline-flex h-9 w-9 items-center justify-center rounded-full border border-[color:var(--color-border-soft)] text-[color:var(--color-text-muted)] transition hover:border-[color:var(--color-accent)] hover:text-[color:var(--color-accent-bright)]"
        >
          <span aria-hidden="true" className="text-xl leading-none">
            ×
          </span>
        </button>
        {children}
      </div>
    </div>
  );
}
