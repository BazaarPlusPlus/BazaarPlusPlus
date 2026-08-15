import clsx from 'clsx';
import { forwardRef, type ButtonHTMLAttributes } from 'react';

export type ButtonVariant = 'default' | 'primary' | 'danger' | 'ghost';
export type ButtonSize = 'small' | 'default' | 'large' | 'icon';

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant;
  size?: ButtonSize;
  busy?: boolean;
  busyLabel?: string;
}

export const Button = forwardRef<HTMLButtonElement, ButtonProps>(
  function Button(
    {
      variant = 'default',
      size = 'default',
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
    return (
      <button
        {...buttonProps}
        ref={ref}
        type={type}
        disabled={disabled || busy}
        aria-busy={busy || undefined}
        data-busy={busy || undefined}
        className={clsx(
          'bpp-button bpp-ui-button',
          `bpp-ui-button-${variant}`,
          `bpp-ui-button-${size}`,
          className
        )}
      >
        {busyLabel ? (
          <span className="bpp-busy-label-sizer">
            <span
              className="bpp-busy-label-idle"
              aria-hidden={busy ? true : undefined}
            >
              {children}
            </span>
            <span
              className="bpp-busy-label-active"
              aria-hidden={!busy ? true : undefined}
            >
              {busyLabel}
            </span>
          </span>
        ) : (
          children
        )}
      </button>
    );
  }
);
