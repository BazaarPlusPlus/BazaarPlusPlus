import clsx from 'clsx';
import { Loader2 } from 'lucide-react';
import { forwardRef, type ButtonHTMLAttributes, type ReactNode } from 'react';

export type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger';
export type ButtonSize = 'md' | 'sm' | 'icon' | 'icon-sm';

/** Class list for anything that should look like a Button, such as a router
 *  `Link` or an external `<a>`. */
export function buttonClassName({
  variant = 'secondary',
  size = 'md',
  className
}: {
  variant?: ButtonVariant;
  size?: ButtonSize;
  className?: string;
} = {}): string {
  return clsx('bpp-btn', `bpp-btn-${variant}`, `bpp-btn-${size}`, className);
}

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant;
  size?: ButtonSize;
  /** Leading icon. Replaced by a spinner while `busy`. */
  icon?: ReactNode;
  busy?: boolean;
  /** Label shown while busy; the button keeps the wider label's width. */
  busyLabel?: string;
}

export const Button = forwardRef<HTMLButtonElement, ButtonProps>(
  function Button(
    {
      variant = 'secondary',
      size = 'md',
      icon,
      busy = false,
      busyLabel,
      disabled,
      type = 'button',
      className,
      children,
      ...buttonProps
    },
    ref
  ) {
    const leading = busy ? (
      <Loader2 className="bpp-spin" aria-hidden="true" />
    ) : (
      icon
    );
    return (
      <button
        {...buttonProps}
        ref={ref}
        type={type}
        disabled={disabled || busy}
        aria-busy={busy || undefined}
        data-busy={busy || undefined}
        className={buttonClassName({ variant, size, className })}
      >
        {leading}
        {busyLabel ? (
          <span className="bpp-busy-label">
            <span aria-hidden={busy ? true : undefined}>{children}</span>
            <span aria-hidden={!busy ? true : undefined}>{busyLabel}</span>
          </span>
        ) : (
          children
        )}
      </button>
    );
  }
);
