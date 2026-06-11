import type { ReactNode } from 'react';
import { PageHeader } from './PageHeader';

export function PageShell({
  eyebrow,
  title,
  action,
  children
}: {
  eyebrow: string;
  title: string;
  action?: ReactNode;
  children: ReactNode;
}) {
  return (
    <div className="flex flex-col gap-6 w-full h-full max-w-5xl mx-auto">
      <PageHeader eyebrow={eyebrow} title={title} action={action} />
      {children}
    </div>
  );
}
