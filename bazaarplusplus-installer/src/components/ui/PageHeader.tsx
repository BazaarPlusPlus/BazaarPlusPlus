import type { ReactNode } from 'react';

export function PageHeader({
  eyebrow,
  title,
  action
}: {
  /** Only when it adds a dimension the title lacks (e.g. a category). */
  eyebrow?: string;
  title: string;
  action?: ReactNode;
}) {
  return (
    <div className="bpp-page-header">
      {eyebrow && <p className="bpp-page-eyebrow">{eyebrow}</p>}
      {action ? (
        <div className="flex items-center justify-between">
          <Title>{title}</Title>
          {action}
        </div>
      ) : (
        <Title>{title}</Title>
      )}
    </div>
  );
}

function Title({ children }: { children: ReactNode }) {
  return (
    <h2 data-page-heading tabIndex={-1} className="bpp-page-title">
      {children}
    </h2>
  );
}
