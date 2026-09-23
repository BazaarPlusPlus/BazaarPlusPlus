import clsx from 'clsx';
import type { ReactNode } from 'react';
import { PageHeader } from './PageHeader';

export function PageShell({
  title,
  leading,
  meta,
  action,
  className,
  children
}: {
  title: string;
  leading?: ReactNode;
  meta?: ReactNode;
  action?: ReactNode;
  className?: string;
  children: ReactNode;
}) {
  return (
    <div className={clsx('bpp-page', className)}>
      <PageHeader title={title} leading={leading} meta={meta} action={action} />
      {children}
    </div>
  );
}
