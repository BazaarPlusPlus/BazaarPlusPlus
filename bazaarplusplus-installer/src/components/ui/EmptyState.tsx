import type { ReactNode } from 'react';

export interface EmptyStateProps {
  icon: ReactNode;
  heading: ReactNode;
  description: ReactNode;
  primaryAction?: ReactNode;
  secondaryAction?: ReactNode;
}

export function EmptyState({
  icon,
  heading,
  description,
  primaryAction,
  secondaryAction
}: EmptyStateProps) {
  return (
    <section className="bpp-panel bpp-empty-state">
      <div className="bpp-empty-state-icon" aria-hidden="true">
        {icon}
      </div>
      <h3 className="bpp-empty-state-heading">{heading}</h3>
      <p className="bpp-empty-state-description">{description}</p>
      {(primaryAction || secondaryAction) && (
        <div className="bpp-empty-state-actions">
          {primaryAction}
          {secondaryAction}
        </div>
      )}
    </section>
  );
}
