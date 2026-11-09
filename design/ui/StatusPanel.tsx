import type { ReactNode } from 'react';
import { cx } from './cx';
import { AlertIcon, CompassIcon, InboxIcon, SpinnerIcon } from './icons';

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
  /** The title's heading level in the page outline. Level 1 is a page-sized
   *  title, for a status that replaces the whole page. */
  headingLevel?: 1 | 2 | 3;
  /** Accessible name of the loading mark, which then reads as a progress bar. */
  progressLabel?: string;
  className?: string;
}

const DEFAULT_MARK = {
  empty: <InboxIcon />,
  error: <AlertIcon />,
  'not-found': <CompassIcon />
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
  headingLevel = 2,
  progressLabel,
  className
}: StatusPanelProps) {
  const loading = kind === 'loading';
  const error = kind === 'error';
  const classes = cx(
    'bpp-status-panel',
    compact && 'is-compact',
    framed && 'bpp-panel',
    className
  );
  const markClass = cx(
    'bpp-status-panel-mark',
    loading && 'is-loading',
    error && 'is-error'
  );
  const mark =
    loading && progressLabel ? (
      <span role="progressbar" aria-label={progressLabel} className={markClass}>
        <SpinnerIcon />
      </span>
    ) : (
      <span className={markClass} aria-hidden="true">
        {loading ? <SpinnerIcon /> : (icon ?? DEFAULT_MARK[kind])}
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
        {mark}
        <span>{title}</span>
      </div>
    );
  }

  const Heading = `h${headingLevel}` as const;
  const pageTitle = headingLevel === 1;
  return (
    <section
      role={kind === 'empty' ? undefined : 'status'}
      aria-live={kind === 'empty' ? undefined : 'polite'}
      aria-busy={loading || undefined}
      className={classes}
    >
      {mark}
      <Heading className={cx('bpp-status-panel-title', pageTitle && 'is-page')}>
        {title}
      </Heading>
      {body && <p className="bpp-status-panel-body">{body}</p>}
      {actions && <div className="bpp-status-panel-actions">{actions}</div>}
    </section>
  );
}
