import clsx from 'clsx';
import { CircleAlert, Compass, Inbox, Loader2 } from 'lucide-react';
import type { ReactNode } from 'react';

export type StatusPanelKind = 'loading' | 'empty' | 'error' | 'not-found';

export interface StatusPanelProps {
  kind: StatusPanelKind;
  /** Replaces the kind's default mark; loading always spins. */
  icon?: ReactNode;
  title: ReactNode;
  /** At most one sentence: what will fill the space. */
  body?: ReactNode;
  actions?: ReactNode;
  /** Single-line loading row: the spinner and the title. */
  compact?: boolean;
  /** Draw the panel frame; off when the status sits inside another panel. */
  framed?: boolean;
  className?: string;
}

const DEFAULT_MARK = {
  empty: <Inbox />,
  error: <CircleAlert />,
  'not-found': <Compass />
} as const;

/** The one layout for loading, empty, error and not-found states. */
export function StatusPanel({
  kind,
  icon,
  title,
  body,
  actions,
  compact = false,
  framed = true,
  className
}: StatusPanelProps) {
  const loading = kind === 'loading';
  const mark = loading ? (
    <Loader2 className="bpp-spin" />
  ) : (
    (icon ?? DEFAULT_MARK[kind])
  );
  const classes = clsx(
    'bpp-status-panel',
    compact && 'is-compact',
    framed && 'bpp-panel',
    className
  );
  const markNode = (
    <span
      className={clsx(
        'bpp-status-panel-mark',
        loading && 'is-loading',
        kind === 'error' && 'is-error'
      )}
      aria-hidden="true"
    >
      {mark}
    </span>
  );

  if (compact) {
    return (
      <div
        role="status"
        aria-live="polite"
        aria-busy={loading || undefined}
        className={classes}
      >
        {markNode}
        <span>{title}</span>
      </div>
    );
  }

  return (
    <section
      role={kind === 'empty' ? undefined : 'status'}
      aria-live={kind === 'empty' ? undefined : 'polite'}
      aria-busy={loading || undefined}
      className={classes}
    >
      {markNode}
      <h3 className="bpp-status-panel-title">{title}</h3>
      {body && <p className="bpp-status-panel-body">{body}</p>}
      {actions && <div className="bpp-status-panel-actions">{actions}</div>}
    </section>
  );
}
