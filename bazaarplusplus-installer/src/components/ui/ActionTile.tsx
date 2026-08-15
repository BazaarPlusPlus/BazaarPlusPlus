import clsx from 'clsx';
import type { ButtonHTMLAttributes, ReactNode } from 'react';

export interface ActionTileProps extends Omit<
  ButtonHTMLAttributes<HTMLButtonElement>,
  'title'
> {
  icon: ReactNode;
  title: ReactNode;
  description: ReactNode;
  danger?: boolean;
  busy?: boolean;
}

export function ActionTile({
  icon,
  title,
  description,
  danger = false,
  busy = false,
  disabled,
  type = 'button',
  className,
  ...buttonProps
}: ActionTileProps) {
  return (
    <button
      {...buttonProps}
      type={type}
      disabled={disabled || busy}
      aria-busy={busy || undefined}
      data-busy={busy || undefined}
      className={clsx(
        'bpp-action-tile',
        danger && 'bpp-action-tile-danger',
        className
      )}
    >
      <span className="bpp-action-tile-icon" aria-hidden="true">
        {icon}
      </span>
      <span className="bpp-action-tile-copy">
        <span className="bpp-action-tile-title">{title}</span>
        <span className="bpp-action-tile-description">{description}</span>
      </span>
    </button>
  );
}
