import { useLayoutEffect, useRef, useState, type ReactNode } from 'react';
import { Button } from './Button';
import { cx } from './cx';
import { CloseIcon } from './icons';

export interface DialogProps {
  onClose: () => void;
  labelledBy?: string;
  /** Whether Escape and a click on the backdrop close the dialog. Turn it off
   *  while the dialog guards work that must finish. */
  dismissible?: boolean;
  /** Focus the dialog itself instead of its first control. */
  focusContainerOnOpen?: boolean;
  className?: string;
  children: ReactNode;
}

/**
 * Modal dialog backed by the native <dialog> element. showModal() gives the
 * platform's role="dialog" + aria-modal, the focus trap, initial focus and a
 * top-layer backdrop. The page behind it does not scroll, and focus returns to
 * the control that opened it.
 *
 * Mount it to open and unmount it to close; Escape and backdrop clicks call
 * onClose so the parent drives the open state.
 */
export function Dialog({
  onClose,
  labelledBy,
  dismissible = true,
  focusContainerOnOpen = false,
  className,
  children
}: DialogProps) {
  const ref = useRef<HTMLDialogElement>(null);
  // The control that opened the dialog, read once before the first effect: in
  // StrictMode the effect runs twice, and the second run would see focus
  // already inside the dialog.
  const [opener] = useState(() =>
    typeof document !== 'undefined' &&
    document.activeElement instanceof HTMLElement
      ? document.activeElement
      : null
  );

  // A layout effect, so the cleanup runs while the dialog is still in the
  // document: close() and the focus return both need it attached.
  useLayoutEffect(() => {
    const el = ref.current;
    if (!el) return;
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    if (!el.open) {
      el.showModal();
      if (focusContainerOnOpen) el.focus({ preventScroll: true });
    }
    return () => {
      if (el.open) el.close();
      document.body.style.overflow = previousOverflow;
      if (opener?.isConnected) opener.focus({ preventScroll: true });
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
      className={cx('bpp-dialog', className)}
      tabIndex={focusContainerOnOpen ? -1 : undefined}
      aria-labelledby={labelledBy}
      onCancel={(event) => {
        // Escape fires `cancel`; the parent owns the close so its state stays in sync.
        event.preventDefault();
        if (dismissible) onClose();
      }}
      onClick={(event) => {
        // A click on the dialog itself (the backdrop area around the card) closes it.
        if (dismissible && event.target === event.currentTarget) onClose();
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
    <div className={cx('bpp-dialog-card', `is-${size}`, className)}>
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
          icon={<CloseIcon />}
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
  return <div className={cx('bpp-dialog-body', className)}>{children}</div>;
}

export function DialogFooter({ children }: { children: ReactNode }) {
  return <div className="bpp-dialog-footer">{children}</div>;
}
