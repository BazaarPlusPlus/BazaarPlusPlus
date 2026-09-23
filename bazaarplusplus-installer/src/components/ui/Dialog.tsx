import clsx from 'clsx';
import { X } from 'lucide-react';
import { useEffect, useRef, type ReactNode } from 'react';
import { useActiveModalDismissalPolicy } from './ModalCoordinator';
import { Button } from './Button';

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
  onClose: () => void;
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
    // Opening is a mount-time side effect; the dialog must not reopen or steal
    // focus again when props change.
    // oxlint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return (
    // Keyboard dismissal is the native Escape -> `cancel` path below; the click
    // handler only adds the pointer-only backdrop affordance.
    // oxlint-disable-next-line jsx-a11y/click-events-have-key-events, jsx-a11y/no-noninteractive-element-interactions
    <dialog
      ref={ref}
      className={`bpp-dialog ${className}`.trim()}
      tabIndex={focusContainerOnOpen ? -1 : undefined}
      aria-labelledby={labelledBy}
      onCancel={(event) => {
        // Escape fires `cancel`; we own the close so the parent state stays in sync.
        event.preventDefault();
        if (!dismissalBlocked) onClose();
      }}
      onClick={(event) => {
        // A click on the dialog itself (the backdrop area around the card) closes it.
        if (!dismissalBlocked && event.target === event.currentTarget) {
          onClose();
        }
      }}
    >
      {children}
    </dialog>
  );
}

/** The one dialog frame: card, header, body and footer. */
export function DialogCard({
  size = 'md',
  className,
  children
}: {
  size?: 'sm' | 'md' | 'lg';
  className?: string;
  children: ReactNode;
}) {
  return (
    <div className={clsx('bpp-dialog-card', `is-${size}`, className)}>
      {children}
    </div>
  );
}

export function DialogHeader({
  titleId,
  title,
  subtitle,
  icon,
  tone = 'neutral',
  onClose,
  closeDisabled = false,
  closeLabel
}: {
  titleId: string;
  title: ReactNode;
  subtitle?: ReactNode;
  icon?: ReactNode;
  tone?: 'neutral' | 'accent' | 'danger';
  onClose?: () => void;
  closeDisabled?: boolean;
  closeLabel?: string;
}) {
  return (
    <div className="bpp-dialog-header">
      {icon && (
        <span className={`bpp-dialog-icon is-${tone}`} aria-hidden="true">
          {icon}
        </span>
      )}
      <div className="bpp-dialog-heading">
        <h2 id={titleId} className="bpp-dialog-title">
          {title}
        </h2>
        {subtitle && <p className="bpp-dialog-subtitle">{subtitle}</p>}
      </div>
      {onClose && (
        <Button
          variant="ghost"
          size="icon-sm"
          onClick={onClose}
          disabled={closeDisabled}
          aria-label={closeLabel}
          icon={<X aria-hidden="true" />}
        />
      )}
    </div>
  );
}

export function DialogBody({
  className,
  children
}: {
  className?: string;
  children: ReactNode;
}) {
  return <div className={clsx('bpp-dialog-body', className)}>{children}</div>;
}

export function DialogFooter({ children }: { children: ReactNode }) {
  return <div className="bpp-dialog-footer">{children}</div>;
}
