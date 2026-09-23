import type { ReactNode } from 'react';
import {
  formatProblemDiagnostic,
  type UiProblem
} from '../../features/shared/problems';
import { useI18n } from '../../i18n/LocaleProvider';
import { Button } from './Button';
import { StatusBanner } from './StatusBanner';

/**
 * The one way a page shows a semantic problem: localized message, optional
 * technical details from the problem itself, and an optional retry. Feature
 * code supplies only the message and any feature-specific extra actions.
 */
export function ProblemBanner({
  message,
  problem,
  tone = 'error',
  onRetry,
  retryBusy = false,
  retryBusyLabel,
  actions
}: {
  message: string;
  problem?: UiProblem | null;
  tone?: 'error' | 'warning';
  onRetry?: () => void;
  retryBusy?: boolean;
  retryBusyLabel?: string;
  /** Extra feature actions, rendered before Retry. */
  actions?: ReactNode;
}) {
  const { t } = useI18n();
  const error = tone === 'error';
  const diagnostic = problem?.diagnostic
    ? formatProblemDiagnostic(problem)
    : null;
  const retry = onRetry ? (
    <Button
      size="sm"
      onClick={onRetry}
      busy={retryBusy}
      busyLabel={retryBusyLabel}
    >
      {t('retry')}
    </Button>
  ) : null;

  return (
    <StatusBanner
      tone={tone}
      role={error ? 'alert' : 'status'}
      aria-live={error ? 'assertive' : 'polite'}
      message={<p className="m-0">{message}</p>}
      actions={
        actions || retry ? (
          <>
            {actions}
            {retry}
          </>
        ) : undefined
      }
      diagnostic={diagnostic ?? undefined}
      diagnosticLabel={t('problemDiagnostics')}
    />
  );
}
