import { useEffect, useRef, type ReactNode } from 'react';

/**
 * Modal dialog backed by the native <dialog> element. showModal() gives us the
 * platform behaviours the installer was missing — implicit role="dialog" +
 * aria-modal, Escape-to-close, a focus trap, initial focus, and a real
 * top-layer backdrop — instead of reimplementing them by hand.
 *
 * Mount it when open and unmount to close; Escape and backdrop clicks call
 * onClose so the parent can drive the open state.
 */
export function Dialog({
  onClose,
  labelledBy,
  className = '',
  children
}: {
  onClose: () => void;
  labelledBy?: string;
  className?: string;
  children: ReactNode;
}) {
  const ref = useRef<HTMLDialogElement>(null);

  useEffect(() => {
    const el = ref.current;
    if (el && !el.open) el.showModal();
    return () => {
      if (el?.open) el.close();
    };
  }, []);

  return (
    <dialog
      ref={ref}
      className={`bpp-dialog ${className}`.trim()}
      aria-labelledby={labelledBy}
      onCancel={(event) => {
        // Escape fires `cancel`; we own the close so the parent state stays in sync.
        event.preventDefault();
        onClose();
      }}
      onClick={(event) => {
        // A click on the dialog itself (the backdrop area around the card) closes it.
        if (event.target === event.currentTarget) onClose();
      }}
    >
      {children}
    </dialog>
  );
}
