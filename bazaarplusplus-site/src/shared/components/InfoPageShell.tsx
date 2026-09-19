import type { ReactNode } from 'react';

import type { Locale, ResolvedSpaLocation } from '../../app/router';
import FooterCredit from './FooterCredit';
import SiteHeader from './SiteHeader';

type InfoPageShellProps = {
  locale: Locale;
  location: ResolvedSpaLocation;
  title: string;
  intro?: string;
  children: ReactNode;
};

export default function InfoPageShell({
  locale,
  location,
  title,
  intro,
  children,
}: InfoPageShellProps) {
  return (
    <div className="relative min-h-screen">
      <SiteHeader location={location} />

      <main className="relative mx-auto flex w-full max-w-5xl flex-col gap-12 px-6 py-12 sm:px-10 sm:py-16">
        <section>
          <div className="grid gap-5">
            <div className="max-w-2xl">
              <h1 className="font-display text-[2.4rem] font-semibold leading-[1.05] tracking-[-0.025em] text-[color:var(--color-text-base)] sm:text-[2.85rem]">
                {title}
              </h1>
              {intro ? (
                <p className="mt-4 max-w-xl text-sm leading-7 text-[color:var(--color-text-muted)]">
                  {intro}
                </p>
              ) : null}
            </div>
          </div>
        </section>

        {children}

        <footer className="mt-6 flex flex-wrap items-center justify-between gap-3 border-t border-[color:var(--color-border-soft)] pt-6 text-[0.72rem] uppercase tracking-[0.18em] text-[color:var(--color-text-faint)]">
          <FooterCredit locale={locale} />
        </footer>
      </main>
    </div>
  );
}
