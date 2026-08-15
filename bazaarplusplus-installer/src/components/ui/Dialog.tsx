import { useEffect, useRef, type ReactNode } from 'react';
import { useActiveModalDismissalPolicy } from './ModalCoordinator';

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
  focusContainerOnOpen = false,
  className = '',
  children
}: {
  onClose: (reason: DialogCloseReason) => void;
  labelledBy?: string;
  focusContainerOnOpen?: boolean;
  className?: string;
  children: ReactNode;
}) {
  const ref = useRef<HTMLDialogElement>(null);
  const dismissalPolicy = useActiveModalDismissalPolicy();
  const dismissalBlocked = dismissalPolicy === 'blocked';

  useEffect(() => {
    const el = ref.current;
    if (el && !el.open) {
      el.showModal();
      if (focusContainerOnOpen) el.focus({ preventScroll: true });
    }
    return () => {
      if (el?.open) el.close();
    };
  }, []);

  return (
    <dialog
      ref={ref}
      className={`bpp-dialog ${className}`.trim()}
      tabIndex={focusContainerOnOpen ? -1 : undefined}
      aria-labelledby={labelledBy}
      onCancel={(event) => {
        // Escape fires `cancel`; we own the close so the parent state stays in sync.
        event.preventDefault();
        if (!dismissalBlocked) onClose('escape');
      }}
      onClick={(event) => {
        // A click on the dialog itself (the backdrop area around the card) closes it.
        if (!dismissalBlocked && event.target === event.currentTarget) {
          onClose('backdrop');
        }
      }}
    >
      {children}
    </dialog>
  );
}

export type DialogCloseReason = 'escape' | 'backdrop';
