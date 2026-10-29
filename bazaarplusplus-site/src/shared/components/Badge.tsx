import type { ReactNode } from 'react';

type BadgeTone = 'neutral' | 'warn';

const TONES: Record<BadgeTone, string> = {
  neutral: 'bg-hover text-text-2',
  warn: 'bg-warning-subtle text-warning',
};

/**
 * The one 24px label pill. Neutral shows a fact without a dot; a status tone shows dot + word
 * (design/components/Badge).
 */
export default function Badge({
  tone = 'neutral',
  children,
}: {
  tone?: BadgeTone;
  children: ReactNode;
}) {
  return (
    <span
      className={`inline-flex h-6 shrink-0 items-center gap-1.5 rounded-full px-2 text-sm font-medium whitespace-nowrap ${TONES[tone]}`}
    >
      {tone === 'neutral' ? null : (
        <span aria-hidden="true" className="size-1.5 shrink-0 rounded-full bg-current" />
      )}
      {children}
    </span>
  );
}
