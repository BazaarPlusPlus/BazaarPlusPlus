import type { ReactNode } from 'react';
import { StatusBanner } from './StatusBanner';

export function ProblemBanner({
  message,
  tone = 'error',
  actions,
  diagnostic,
  diagnosticLabel
}: {
  message: string;
  tone?: 'error' | 'warning';
  actions?: ReactNode;
  diagnostic?: string | null;
  diagnosticLabel?: string;
}) {
  const error = tone === 'error';
  return (
    <StatusBanner
      tone={tone}
      role={error ? 'alert' : 'status'}
      aria-live={error ? 'assertive' : 'polite'}
      message={<p className="m-0">{message}</p>}
      actions={actions}
      diagnostic={diagnostic || undefined}
      diagnosticLabel={diagnosticLabel}
    />
  );
}
