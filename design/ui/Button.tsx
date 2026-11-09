import type {
  AnchorHTMLAttributes,
  ButtonHTMLAttributes,
  MouseEvent,
  ReactNode,
  Ref
} from 'react';
import { cx } from './cx';
import { SpinnerIcon } from './icons';

export type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger';
export type ButtonSize = 'md' | 'sm' | 'icon' | 'icon-sm';

/** Class list for anything that should look like a Button, such as a router
 *  `Link`. */
export function buttonClassName({
  variant = 'secondary',
  size = 'md',
  className
}: {
  variant?: ButtonVariant;
  size?: ButtonSize;
  className?: string;
} = {}): string {
  return cx('bpp-btn', `bpp-btn-${variant}`, `bpp-btn-${size}`, className);
}

interface SharedProps {
  variant?: ButtonVariant;
  size?: ButtonSize;
  /** Leading icon. Replaced by a spinner while `busy`. */
  icon?: ReactNode;
  /** Shows a spinner and blocks activation. */
  busy?: boolean;
  /** Label shown while busy; the button keeps the wider label's width. */
  busyLabel?: ReactNode;
  disabled?: boolean;
  className?: string;
  children?: ReactNode;
}

export type ButtonElementProps = SharedProps &
  Omit<ButtonHTMLAttributes<HTMLButtonElement>, keyof SharedProps> & {
    href?: undefined;
    ref?: Ref<HTMLButtonElement>;
  };

/** A link drawn as a button. While disabled or busy it leaves the tab order
 *  and ignores clicks. */
export type LinkButtonProps = SharedProps &
  Omit<AnchorHTMLAttributes<HTMLAnchorElement>, keyof SharedProps> & {
    href: string;
    ref?: Ref<HTMLAnchorElement>;
  };

export type ButtonProps = ButtonElementProps | LinkButtonProps;

function Content({
  icon,
  busy,
  busyLabel,
  children
}: Pick<SharedProps, 'icon' | 'busy' | 'busyLabel' | 'children'>) {
  return (
    <>
      {busy ? <SpinnerIcon /> : icon}
      {busyLabel ? (
        <span className="bpp-busy-label">
          <span aria-hidden={busy ? true : undefined}>{children}</span>
          <span aria-hidden={busy ? undefined : true}>{busyLabel}</span>
        </span>
      ) : (
        children
      )}
    </>
  );
}

export function Button(props: ButtonProps) {
  if (props.href !== undefined) {
    const {
      variant,
      size,
      icon,
      busy = false,
      busyLabel,
      disabled = false,
      className,
      children,
      href,
      onClick,
      tabIndex,
      ...anchorProps
    } = props;
    const inert = disabled || busy;
    return (
      <a
        {...anchorProps}
        href={href}
        aria-disabled={inert || undefined}
        aria-busy={busy || undefined}
        data-busy={busy || undefined}
        tabIndex={inert ? -1 : tabIndex}
        onClick={(event: MouseEvent<HTMLAnchorElement>) => {
          if (inert) {
            event.preventDefault();
            return;
          }
          onClick?.(event);
        }}
        className={buttonClassName({ variant, size, className })}
      >
        <Content icon={icon} busy={busy} busyLabel={busyLabel}>
          {children}
        </Content>
      </a>
    );
  }

  const {
    variant,
    size,
    icon,
    busy = false,
    busyLabel,
    disabled = false,
    type = 'button',
    className,
    children,
    href: _href,
    ...buttonProps
  } = props;
  return (
    <button
      {...buttonProps}
      // oxlint-disable-next-line react/button-has-type -- `type` defaults to "button" above.
      type={type}
      disabled={disabled || busy}
      aria-busy={busy || undefined}
      data-busy={busy || undefined}
      className={buttonClassName({ variant, size, className })}
    >
      <Content icon={icon} busy={busy} busyLabel={busyLabel}>
        {children}
      </Content>
    </button>
  );
}
