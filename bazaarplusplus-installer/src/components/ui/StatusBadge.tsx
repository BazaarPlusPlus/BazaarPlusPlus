import clsx from 'clsx';
import type { ReactNode } from 'react';

export type StatusBadgeTone =
  'ok' | 'warn' | 'bad' | 'busy' | 'neutral' | 'accent';

/** A status reads as dot + word; the tone colour is the third signal. */
export function StatusBadge({
  tone = 'neutral',
  dot = true,
  className,
  children
}: {
  tone?: StatusBadgeTone;
  dot?: boolean;
  className?: string;
  children: ReactNode;
}) {
  return (
    <span
      className={clsx(
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
