import { getSiteCopy } from '../content/site-copy';
import SiteHeader from '../shared/components/SiteHeader';
import type { ResolvedSpaLocation } from './router';

type LoadingScreenProps = {
  location: ResolvedSpaLocation;
};

export function LoadingScreen({ location }: LoadingScreenProps) {
  const copy = getSiteCopy(location.locale);
  const loadingCopy = copy.common.loading;
  const heroCopy = copy.stats.heroes;

  return (
    <div className="relative min-h-screen">
      <SiteHeader location={location} />

      <main
        aria-busy="true"
        className="relative mx-auto flex min-w-0 w-full max-w-[1440px] flex-col gap-10 px-6 py-8 sm:w-[calc(100%-5rem)] sm:px-10 sm:py-10 2xl:px-12"
      >
        <section>
          <p className="eyebrow eyebrow-rule">{heroCopy.eyebrow}</p>
          <h1 className="mt-3 font-display text-[2.2rem] font-semibold leading-[1.05] tracking-[-0.025em] text-[color:var(--color-text-base)] sm:text-[2.7rem]">
            {heroCopy.title}
          </h1>
        </section>

        <section
          aria-live="polite"
          className="surface relative flex min-h-[360px] items-center justify-center overflow-hidden px-6 py-14 sm:min-h-[420px] sm:px-10"
        >
          <div
            aria-hidden="true"
            className="pointer-events-none absolute inset-0 bg-[radial-gradient(ellipse_at_50%_20%,rgba(232,185,74,0.10),transparent_58%)]"
          />

          <div className="relative flex w-full max-w-xl flex-col items-center text-center">
            <div
              aria-hidden="true"
              className="relative mb-7 flex h-16 w-16 items-center justify-center"
            >
              <span className="absolute inset-0 rounded-full border border-[color:var(--color-border-bright)] opacity-60 motion-safe:animate-pulse" />
              <span className="absolute inset-2 rounded-full border border-[color:var(--color-border-soft)]" />
              <span className="h-2.5 w-2.5 rounded-full bg-[color:var(--color-accent)] shadow-[0_0_24px_rgba(232,185,74,0.75)]" />
            </div>

            <p className="eyebrow">{heroCopy.snapshot.label}</p>
            <h2 className="mt-3 font-display text-2xl font-semibold tracking-tight text-[color:var(--color-text-base)] sm:text-3xl">
              {loadingCopy.title}
            </h2>
            <p className="mt-3 max-w-md text-sm leading-6 text-[color:var(--color-text-muted)]">
              {loadingCopy.body}
            </p>

            <div
              role="progressbar"
              aria-label={loadingCopy.progressAriaLabel}
              className="mt-8 h-1 w-full max-w-sm overflow-hidden rounded-full bg-[color:var(--color-border-soft)]"
            >
              <div className="shimmer h-full w-full rounded-full" />
            </div>
          </div>
        </section>
      </main>
    </div>
  );
}

export function ErrorScreen({
  error,
  location,
}: {
  error: unknown;
  location: ResolvedSpaLocation;
}) {
  const { locale } = location;
  const copy = getSiteCopy(locale).common.error;
  const message = error instanceof Error ? error.message : copy.unknownMessage;

  return (
    <main className="mx-auto flex min-h-screen w-full max-w-3xl flex-col justify-center gap-4 px-6 py-10">
      <h1 className="font-display text-5xl font-semibold tracking-tight text-[color:var(--color-text-base)]">
        {copy.title}
      </h1>
      <p className="mt-2 max-w-xl text-sm leading-6 text-[color:var(--color-text-muted)]">
        {message}
      </p>
      <a
        className="mt-4 inline-flex w-fit items-center gap-2 rounded-full border border-[color:var(--color-border-soft)] px-4 py-2 text-sm font-medium text-[color:var(--color-accent-bright)] transition hover:border-[color:var(--color-accent)]"
        href={location.navigation.heroesHref}
      >
        {copy.backToHeroes}
      </a>
    </main>
  );
}

export function NotFoundScreen({ location }: { location: ResolvedSpaLocation }) {
  const { locale } = location;
  const copy = getSiteCopy(locale).common.notFound;

  return (
    <main className="mx-auto flex min-h-screen w-full max-w-3xl flex-col justify-center gap-4 px-6 py-10">
      <h1 className="font-display text-5xl font-semibold tracking-tight text-[color:var(--color-text-base)]">
        {copy.title}
      </h1>
      <a
        className="mt-4 inline-flex w-fit items-center gap-2 rounded-full border border-[color:var(--color-border-soft)] px-4 py-2 text-sm font-medium text-[color:var(--color-accent-bright)] transition hover:border-[color:var(--color-accent)]"
        href={location.navigation.heroesHref}
      >
        {copy.backToHeroes}
      </a>
    </main>
  );
}
