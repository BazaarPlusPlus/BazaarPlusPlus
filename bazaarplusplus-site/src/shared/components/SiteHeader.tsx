import { getSiteCopy } from '../../content/site-copy';
import type { Locale, ResolvedSpaLocation } from '../../app/router';
import { pageContainerClassName, type PageWidth } from './page-container';

type SiteHeaderProps = {
  location: ResolvedSpaLocation;
  width?: PageWidth;
};

const LOCALE_OPTIONS: Array<{ code: Locale; label: string }> = [
  { code: 'en', label: 'EN' },
  { code: 'zh', label: '中' },
];

export default function SiteHeader({ location, width = 'info' }: SiteHeaderProps) {
  const { locale } = location;
  const copy = getSiteCopy(locale);
  const commonCopy = copy.common;

  return (
    <header
      aria-label={commonCopy.brand.name}
      className="sticky top-0 z-30 border-b border-line bg-sidebar"
    >
      <div
        className={`${pageContainerClassName(width)} flex flex-wrap items-center gap-x-6 gap-y-2 py-2.5`}
      >
        <a
          href={location.navigation.homeHref}
          aria-label={commonCopy.homeAriaLabel}
          className="order-1 flex w-fit items-center gap-2.5 rounded-control text-text-1 no-underline"
        >
          <img
            src="/bazaarplusplus-icon.webp"
            alt=""
            width={28}
            height={28}
            className="size-7 object-contain"
            decoding="sync"
            fetchPriority="high"
          />
          <span className="text-[15px] font-semibold">{commonCopy.brand.name}</span>
          <span aria-hidden="true" className="hidden text-xs text-text-3 lg:inline">
            {commonCopy.brand.subtitle}
          </span>
        </a>

        <nav
          aria-label={commonCopy.primaryNavAriaLabel}
          className="order-3 -mx-1 flex w-full min-w-0 items-center gap-0.5 overflow-x-auto sm:order-2 sm:mx-0 sm:w-auto sm:flex-1"
        >
          {location.navigation.items.map((item) => {
            const active = item.page === location.route.page;
            const label =
              item.group === 'primary'
                ? copy.primaryNav[item.page as 'heroes']
                : copy.secondaryNav[item.page as 'tutorial' | 'download' | 'support'];
            return (
              <a
                key={item.page}
                href={item.href}
                aria-current={active ? 'page' : undefined}
                className={`inline-flex h-8 shrink-0 items-center rounded-control px-3 text-[13px] font-medium no-underline transition-colors duration-(--t-fast) ${
                  active
                    ? 'bg-selected text-text-1'
                    : 'text-text-2 hover:bg-hover hover:text-text-1'
                }`}
              >
                {label}
              </a>
            );
          })}
        </nav>

        <div
          role="group"
          aria-label={commonCopy.languageLabel}
          className="order-2 ml-auto inline-flex items-center gap-0.5 rounded-control border border-line bg-canvas p-0.5 text-xs font-medium sm:order-3 sm:ml-0"
        >
          {LOCALE_OPTIONS.map((option) => {
            const active = option.code === locale;
            if (active) {
              return (
                <span
                  key={option.code}
                  aria-current="true"
                  className="rounded-[4px] bg-selected px-2.5 py-1 text-text-1"
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
                className="rounded-[4px] px-2.5 py-1 text-text-2 no-underline transition-colors duration-(--t-fast) hover:text-text-1"
              >
                {option.label}
              </a>
            );
          })}
        </div>
      </div>
    </header>
  );
}
