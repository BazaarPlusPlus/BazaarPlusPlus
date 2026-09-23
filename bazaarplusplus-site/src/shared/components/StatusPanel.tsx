import type { ReactNode } from 'react';

import { AlertIcon, CompassIcon, InboxIcon } from './icons';

type StatusKind = 'loading' | 'error' | 'empty' | 'not-found';

type StatusPanelProps = {
  kind: StatusKind;
  title: string;
  body?: string;
  /** Accessible name of the indeterminate progress bar; loading only. */
  progressLabel?: string;
  headingLevel?: 1 | 2 | 3;
  /** Draw the panel frame; off when the status sits inside another panel. */
  framed?: boolean;
  actions?: ReactNode;
};

function StatusMark({ kind }: { kind: StatusKind }) {
  if (kind === 'loading') {
    return (
      <span aria-hidden="true" className="flex size-[22px] items-center justify-center">
        <span className="size-2 rounded-full bg-accent motion-safe:animate-pulse" />
      </span>
    );
  }
  if (kind === 'error') {
    return <AlertIcon className="size-[22px] text-danger" />;
  }
  if (kind === 'not-found') {
    return <CompassIcon className="size-[22px] text-text-3" />;
  }
  return <InboxIcon className="size-[22px] text-text-3" />;
}

/** One layout for loading, error, empty, and not-found states: icon, title, body, actions. */
export default function StatusPanel({
  kind,
  title,
  body,
  progressLabel,
  headingLevel = 2,
  framed = true,
  actions,
}: StatusPanelProps) {
  const Heading = `h${headingLevel}` as const;

  return (
    <section
      aria-live={kind === 'loading' || kind === 'error' ? 'polite' : undefined}
      className={`flex flex-col items-center gap-2 px-6 text-center ${
        framed ? 'panel py-14 sm:py-20' : 'py-12'
      }`}
    >
      <StatusMark kind={kind} />
      <Heading
        className={`mt-1 font-semibold text-balance text-text-1 ${
          headingLevel === 1 ? 'text-[22px] leading-tight' : 'text-base'
        }`}
      >
        {title}
      </Heading>
      {body ? <p className="max-w-md text-sm text-text-2">{body}</p> : null}
      {kind === 'loading' ? (
        <div
          role="progressbar"
          aria-label={progressLabel}
          className="mt-4 h-1 w-full max-w-xs overflow-hidden rounded-full bg-hover"
        >
          <div className="h-full w-full bg-accent/50 motion-safe:animate-pulse" />
        </div>
      ) : null}
      {actions ? (
        <div className="mt-3 flex flex-wrap items-center justify-center gap-2">{actions}</div>
      ) : null}
    </section>
  );
}
