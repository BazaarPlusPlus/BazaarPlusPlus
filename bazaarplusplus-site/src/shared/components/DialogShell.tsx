import { useEffect, useRef, type ReactNode } from 'react';

import Button from './Button';
import { CloseIcon } from './icons';

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
    <div className="fixed inset-0 z-50 flex items-center justify-center px-4 py-10">
      <button
        type="button"
        aria-label={closeLabel}
        onClick={onClose}
        className="absolute inset-0 cursor-default bg-black/55"
      />
      <div
        ref={cardRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby={labelledBy}
        tabIndex={-1}
        className="relative w-full max-w-[520px] animate-dialog-in rounded-dialog border border-line-strong bg-panel p-4 shadow-(--shadow-float) outline-none"
      >
        <Button
          variant="ghost"
          size="icon-sm"
          aria-label={closeLabel}
          onClick={onClose}
          // Optical edge: the 14px glyph sits on the card's 16px edge, centred on the title line.
          className="absolute top-[13px] right-2"
          icon={<CloseIcon />}
        />
        {children}
      </div>
    </div>
  );
}
