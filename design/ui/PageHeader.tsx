import type { ReactNode } from 'react';

export function PageHeader({
  title,
  leading,
  meta,
  action
}: {
  title: string;
  /** Rendered before the title, e.g. a back button. */
  leading?: ReactNode;
  /** Short secondary facts beside the title. */
  meta?: ReactNode;
  action?: ReactNode;
}) {
  return (
    <div className="bpp-page-header">
      {leading}
      <h2 data-page-heading tabIndex={-1} className="bpp-page-title">
        {title}
      </h2>
      {meta && <span className="bpp-page-meta">{meta}</span>}
      {action && <div className="bpp-page-actions">{action}</div>}
    </div>
  );
}
