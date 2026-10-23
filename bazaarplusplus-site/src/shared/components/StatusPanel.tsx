import type { ReactNode } from 'react';

import { AlertIcon, CompassIcon, InboxIcon, SpinnerIcon } from './icons';

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

function StatusMark({ kind, progressLabel }: { kind: StatusKind; progressLabel?: string }) {
  if (kind === 'loading') {
    // Busy is always the spinner (design/README.md, Motion); it carries the progressbar role.
    return (
      <span role="progressbar" aria-label={progressLabel} className="flex text-text-2">
        <SpinnerIcon className="size-[22px]" />
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
      className={`flex flex-col items-center gap-2 px-4 py-10 text-center text-text-2 ${
        framed ? 'panel' : ''
      }`}
    >
      <StatusMark kind={kind} progressLabel={progressLabel} />
      <Heading
        className={`mt-1 font-semibold text-balance text-text-1 ${
          headingLevel === 1 ? 'text-[24px] leading-[1.3]' : 'text-lg leading-[1.4]'
        }`}
      >
        {title}
      </Heading>
      {body ? <p className="max-w-[44ch] text-[15px]">{body}</p> : null}
      {actions ? (
        <div className="mt-2 flex flex-wrap items-center justify-center gap-2">{actions}</div>
      ) : null}
    </section>
  );
}
