export type PageWidth = 'info' | 'wide';

const MAX_WIDTH: Record<PageWidth, string> = {
  info: 'max-w-5xl',
  wide: 'max-w-[1440px]',
};

/** Content column shared by the header, page body, and footer so their edges line up. */
export function pageContainerClassName(width: PageWidth): string {
  return `mx-auto w-full min-w-0 ${MAX_WIDTH[width]} px-4 sm:px-8`;
}
