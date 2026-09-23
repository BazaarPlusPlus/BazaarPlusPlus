import type { AnchorHTMLAttributes, ButtonHTMLAttributes, MouseEvent, ReactNode } from 'react';

import { SpinnerIcon } from './icons';

type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger';
type ButtonSize = 'md' | 'sm' | 'icon';

type SharedProps = {
  variant?: ButtonVariant;
  size?: ButtonSize;
  /** Leading icon; replaced by a spinner while `busy`. */
  icon?: ReactNode;
  /** Shows a spinner and blocks activation. */
  busy?: boolean;
  disabled?: boolean;
  className?: string;
  children?: ReactNode;
};

type ButtonElementProps = SharedProps &
  Omit<ButtonHTMLAttributes<HTMLButtonElement>, keyof SharedProps> & { href?: undefined };

type LinkElementProps = SharedProps &
  Omit<AnchorHTMLAttributes<HTMLAnchorElement>, keyof SharedProps> & { href: string };

export type ButtonProps = ButtonElementProps | LinkElementProps;

const BASE =
  'inline-flex min-w-0 shrink-0 cursor-pointer items-center justify-center gap-1.5 whitespace-nowrap rounded-control border font-medium leading-none no-underline transition-colors duration-(--t-fast) ease-(--ease) aria-busy:pointer-events-none aria-disabled:pointer-events-none aria-disabled:opacity-45 disabled:pointer-events-none disabled:opacity-45';

const SIZES: Record<ButtonSize, string> = {
  md: 'h-8 px-3 text-[13px] [&_svg]:size-[15px]',
  sm: 'h-[26px] px-[9px] text-xs [&_svg]:size-[13px]',
  icon: 'size-8 p-0 [&_svg]:size-4',
};

const VARIANTS: Record<ButtonVariant, string> = {
  primary: 'border-transparent bg-accent font-semibold text-on-accent hover:bg-accent-hover',
  secondary: 'border-line-strong bg-panel text-text-1 hover:bg-hover',
  ghost: 'border-transparent text-text-2 hover:bg-hover hover:text-text-1',
  danger: 'border-danger/35 bg-danger-subtle text-danger-text hover:bg-danger/22',
};

export default function Button({
  variant = 'secondary',
  size = 'md',
  icon,
  busy = false,
  disabled = false,
  className = '',
  children,
  ...rest
}: ButtonProps) {
  const classes = `${BASE} ${SIZES[size]} ${VARIANTS[variant]} ${className}`;
  const content = (
    <>
      {busy ? <SpinnerIcon /> : icon}
      {children}
    </>
  );

  if (rest.href != null) {
    const { href, onClick, ...anchorProps } = rest;
    const inert = disabled || busy;
    return (
      <a
        {...anchorProps}
        href={href}
        aria-disabled={disabled || undefined}
        aria-busy={busy || undefined}
        tabIndex={inert ? -1 : anchorProps.tabIndex}
        onClick={(event: MouseEvent<HTMLAnchorElement>) => {
          if (inert) {
            event.preventDefault();
            return;
          }
          onClick?.(event);
        }}
        className={classes}
      >
        {content}
      </a>
    );
  }

  const { onClick, type = 'button', ...buttonProps } = rest;
  return (
    <button
      {...buttonProps}
      type={type}
      disabled={disabled}
      aria-busy={busy || undefined}
      onClick={(event) => {
        if (busy) {
          event.preventDefault();
          return;
        }
        onClick?.(event);
      }}
      className={classes}
    >
      {content}
    </button>
  );
}
