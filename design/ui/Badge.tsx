import type { ReactNode } from 'react';
import { cx } from './cx';

export type BadgeTone = 'ok' | 'warn' | 'bad' | 'busy' | 'neutral' | 'accent';

export interface BadgeProps {
  tone?: BadgeTone;
  /** Pass false for neutral facts such as a region or snapshot label. */
  dot?: boolean;
  className?: string;
  children: ReactNode;
}

export function Badge({
  tone = 'neutral',
  dot = true,
  className,
  children
}: BadgeProps) {
  return (
    <span
      className={cx(
        'bpp-badge',
        `bpp-badge-${tone}`,
        !dot && 'no-dot',
        className
      )}
    >
      {children}
    </span>
  );
}
