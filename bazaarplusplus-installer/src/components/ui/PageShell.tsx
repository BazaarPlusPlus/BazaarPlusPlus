import type { ReactNode } from 'react';
import { PageHeader } from './PageHeader';

export function PageShell({
  eyebrow,
  title,
  action,
  className,
  children
}: {
  eyebrow?: string;
  title: string;
  action?: ReactNode;
  className?: string;
  children: ReactNode;
}) {
  return (
    <div className={`bpp-page ${className ?? ''}`}>
      <PageHeader eyebrow={eyebrow} title={title} action={action} />
      {children}
    </div>
  );
}
