import { getSiteCopy } from '../../content/site-copy';
import type { Locale, ResolvedSpaLocation } from '../../app/router';

type SiteHeaderProps = {
  location: ResolvedSpaLocation;
};

const LOCALE_OPTIONS: Array<{ code: Locale; label: string }> = [
  { code: 'en', label: 'EN' },
  { code: 'zh', label: '中' },
];

export default function SiteHeader({ location }: SiteHeaderProps) {
  const { locale } = location;
  const copy = getSiteCopy(locale);
  const commonCopy = copy.common;

  return (
    <header
      aria-label={commonCopy.brand.name}
      className="sticky top-0 z-30 border-b border-[color:var(--color-border-soft)] bg-[linear-gradient(180deg,rgba(15,12,8,0.96),rgba(10,8,5,0.92))] backdrop-blur-md"
    >
      <div className="pointer-events-none absolute inset-x-0 -bottom-px h-px bg-gradient-to-r from-transparent via-[color:var(--color-accent-glow)] to-transparent" />
      <div className="mx-auto flex w-full max-w-[1440px] flex-wrap items-center justify-center gap-4 px-6 py-4 sm:w-[calc(100%-5rem)] sm:justify-between sm:px-10 2xl:px-12">
        <a
          href={location.navigation.homeHref}
          aria-label={commonCopy.homeAriaLabel}
          className="group flex w-fit items-center gap-3"
        >
          <span className="relative flex h-11 w-11 shrink-0 items-center justify-center">
            <span className="absolute inset-0 rounded-full bg-[radial-gradient(circle,rgba(232,185,74,0.32),transparent_72%)] blur-md transition-opacity group-hover:opacity-100" />
            <img
              src="/bazaarplusplus-icon.webp"
              alt=""
              width={44}
              height={44}
              className="relative h-11 w-11 object-contain drop-shadow-[0_0_18px_rgba(232,185,74,0.32)]"
              decoding="sync"
              fetchPriority="high"
            />
          </span>
          <span className="grid gap-1.5 leading-none">
            <span className="font-display text-[1.32rem] font-semibold tracking-[-0.01em] text-[color:var(--color-accent-bright)]">
              {commonCopy.brand.name}
            </span>
            <span
              aria-hidden="true"
              className="font-display-italic text-[0.62rem] uppercase tracking-[0.28em] text-[color:var(--color-text-faint)]"
            >
              {commonCopy.brand.subtitle}
            </span>
          </span>
        </a>

        <div className="flex w-full min-w-0 flex-wrap items-center justify-center gap-x-4 gap-y-2 text-xs text-[color:var(--color-text-muted)] sm:w-auto sm:flex-1 sm:justify-end">
          <nav
            aria-label={commonCopy.primaryNavAriaLabel}
            className="flex flex-wrap items-center gap-1 rounded-full border border-[color:var(--color-border-soft)] bg-[rgba(255,245,220,0.025)] p-1"
          >
            {location.navigation.items.map((item) => {
              const active = item.page === location.route.page;
              const label =
                item.group === 'primary'
                  ? copy.primaryNav[item.page as 'heroes']
                  : copy.secondaryNav[item.page as 'tutorial' | 'download' | 'support'];
              const labelTypography =
                locale === 'zh'
                  ? 'text-[0.84rem] tracking-normal'
                  : 'text-[0.76rem] tracking-[0.06em]';
              return (
                <a
                  key={item.page}
                  href={item.href}
                  aria-current={active ? 'page' : undefined}
                  className={`inline-flex min-h-8 items-center rounded-full px-3 py-1.5 font-medium transition ${labelTypography} ${
                    active
                      ? 'bg-[rgba(232,185,74,0.16)] text-[color:var(--color-accent-bright)] shadow-[0_0_18px_rgba(232,185,74,0.12)]'
                      : 'text-[color:var(--color-text-muted)] hover:bg-[rgba(255,245,220,0.04)] hover:text-[color:var(--color-text-base)]'
                  }`}
                >
                  <span>{label}</span>
                </a>
              );
            })}
          </nav>

          <div
            role="group"
            aria-label={commonCopy.languageLabel}
            className="inline-flex items-center rounded-full border border-[color:var(--color-border-soft)] p-0.5 text-[0.7rem] font-medium tracking-[0.08em]"
          >
            {LOCALE_OPTIONS.map((option) => {
              const active = option.code === locale;
              if (active) {
                return (
                  <span
                    key={option.code}
                    aria-current="true"
                    className="rounded-full bg-[rgba(232,185,74,0.18)] px-2.5 py-0.5 text-[color:var(--color-accent-bright)]"
                  >
                    {option.label}
                  </span>
                );
              }
              return (
                <a
                  key={option.code}
                  href={location.navigation.localeHrefs[option.code]}
                  hrefLang={option.code}
                  className="rounded-full px-2.5 py-0.5 text-[color:var(--color-text-muted)] transition hover:text-[color:var(--color-text-base)]"
                >
                  {option.label}
                </a>
              );
            })}
          </div>
        </div>
      </div>
    </header>
  );
}
