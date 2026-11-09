import type { ReactNode } from 'react';
import { cx } from './cx';

export type BadgeTone = 'ok' | 'warn' | 'bad' | 'busy' | 'neutral' | 'accent';

export interface BadgeProps {
  tone?: BadgeTone;
  /** A status reads as dot + word. Turn the dot off only for a neutral fact,
   *  such as a region or a snapshot label. */
  dot?: boolean;
  className?: string;
  children: ReactNode;
}

/** The one 24px label pill; the tone colour is the third signal. */
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
