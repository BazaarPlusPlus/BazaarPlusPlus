import type { ReactNode } from 'react';

import type { ResolvedSpaLocation } from '../../app/router';
import PageLayout, { PageHeading } from './PageLayout';

type InfoPageShellProps = {
  location: ResolvedSpaLocation;
  title: string;
  intro?: string;
  children: ReactNode;
};

export default function InfoPageShell({ location, title, intro, children }: InfoPageShellProps) {
  return (
    <PageLayout location={location}>
      <PageHeading title={title} intro={intro} />
      {children}
    </PageLayout>
  );
}
