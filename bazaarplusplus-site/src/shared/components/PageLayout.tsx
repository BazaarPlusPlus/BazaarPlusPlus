import type { ReactNode } from 'react';

import type { ResolvedSpaLocation } from '../../app/router';
import FooterCredit from './FooterCredit';
import { pageContainerClassName, type PageWidth } from './page-container';
import SiteHeader from './SiteHeader';

type PageLayoutProps = {
  location: ResolvedSpaLocation;
  width?: PageWidth;
  /** Extra footer content shown opposite the credit, such as the last sync time. */
  footerMeta?: ReactNode;
  busy?: boolean;
  children: ReactNode;
};

/** Page chrome shared by every route: header, content column, and footer. */
export default function PageLayout({
  location,
  width = 'info',
  footerMeta,
  busy,
  children,
}: PageLayoutProps) {
  const container = pageContainerClassName(width);

  return (
    <div className="flex min-h-screen flex-col">
      <SiteHeader location={location} width={width} />

      <main
        aria-busy={busy || undefined}
        className={`${container} flex flex-1 flex-col gap-10 pt-10 pb-14 sm:gap-12 sm:pt-14`}
      >
        {children}
      </main>

      <footer className={container}>
        <div className="flex flex-wrap items-center justify-between gap-3 border-t border-line py-6 text-xs text-text-3">
          <FooterCredit locale={location.locale} />
          {footerMeta}
        </div>
      </footer>
    </div>
  );
}

type PageHeadingProps = {
  title: string;
  intro?: string;
  actions?: ReactNode;
};

export function PageHeading({ title, intro, actions }: PageHeadingProps) {
  return (
    <section className="flex flex-col gap-5 sm:flex-row sm:items-end sm:justify-between">
      <div className="flex max-w-3xl min-w-0 flex-col gap-3">
        <h1 className="text-[30px] leading-[1.15] font-bold tracking-[-0.025em] text-balance text-text-1 sm:text-[40px]">
          {title}
        </h1>
        {intro ? <p className="max-w-[62ch] text-base text-text-2">{intro}</p> : null}
      </div>
      {actions ? <div className="flex shrink-0 flex-wrap items-center gap-2">{actions}</div> : null}
    </section>
  );
}

/** Section title (h2, 22/650) used below the page heading. */
export function SectionHeading({ id, children }: { id?: string; children: ReactNode }) {
  return (
    <h2
      id={id}
      className="text-[22px] leading-tight font-[650] tracking-[-0.01em] text-balance text-text-1"
    >
      {children}
    </h2>
  );
}
