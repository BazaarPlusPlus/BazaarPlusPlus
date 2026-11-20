import { useLayoutEffect, useRef, useState, type ReactNode } from 'react';
import { Button } from './Button';
import { cx } from './cx';
import { CloseIcon } from './icons';

export interface DialogProps {
  onClose: () => void;
  labelledBy?: string;
  /** Controls Escape and backdrop clicks. Disable while the dialog guards work that must finish. */
  dismissible?: boolean;
  /** Focus the dialog itself instead of its first control. */
  focusContainerOnOpen?: boolean;
  className?: string;
  children: ReactNode;
}

/**
 * Mount to open and unmount to close. Escape and backdrop clicks call onClose.
 * The dialog locks page scroll and restores focus to the opener on close.
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
  // Capture the opener once because a repeated StrictMode effect would see focus inside the dialog.
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
    // The native cancel event handles Escape, so the click handler only covers the backdrop.
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
        if (dismissible && event.target === event.currentTarget) onClose();
      }}
    >
      {children}
    </dialog>
  );
}

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
