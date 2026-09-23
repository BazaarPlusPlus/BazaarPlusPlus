import type { ReactNode } from 'react';

import { getSiteCopy } from '../../content/site-copy';
import type { ResolvedSpaLocation } from '../../app/router';
import { formatDateTime } from '../lib/dashboard';
import PageLayout, { PageHeading } from './PageLayout';

type StatsPageShellProps = {
  location: ResolvedSpaLocation;
  title: string;
  generatedAt: string;
  actions?: ReactNode;
  filters: ReactNode;
  children: ReactNode;
};

export default function StatsPageShell({
  location,
  title,
  generatedAt,
  actions,
  filters,
  children,
}: StatsPageShellProps) {
  const { locale } = location;
  const commonCopy = getSiteCopy(locale).common;

  return (
    <PageLayout
      location={location}
      width="wide"
      footerMeta={
        <span className="tabular-nums">
          {commonCopy.lastSync} · {formatDateTime(generatedAt, locale)}
        </span>
      }
    >
      <PageHeading title={title} actions={actions} />
      {filters}
      {children}
    </PageLayout>
  );
}
