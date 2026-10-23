export type PageWidth = 'info' | 'wide';

const MAX_WIDTH: Record<PageWidth, string> = {
  info: 'max-w-5xl',
  wide: 'max-w-[1440px]',
};

/** Container widths for site chrome and page content. */
export function pageContainerClassName(width: PageWidth): string {
  return `mx-auto w-full min-w-0 ${MAX_WIDTH[width]} px-4 sm:px-8`;
}
