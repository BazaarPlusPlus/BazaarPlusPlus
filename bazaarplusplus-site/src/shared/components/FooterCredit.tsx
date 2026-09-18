import { getSiteCopy } from '../../content/site-copy';
import type { Locale } from '../../app/router';

const AUTHOR_URL = 'https://github.com/cauyxy';

export default function FooterCredit({ locale }: { locale: Locale }) {
  const copy = getSiteCopy(locale).common.footer;

  return (
    <span className="inline-flex items-center gap-1.5 font-display-italic normal-case tracking-[0.08em]">
      <span>{copy.madeWith}</span>
      <svg
        aria-label={copy.love}
        className="h-3.5 w-3.5 text-[#f1749e] drop-shadow-[0_0_8px_rgba(241,116,158,0.36)]"
        fill="currentColor"
        role="img"
        viewBox="0 0 24 24"
      >
        <path d="M12 21.2 10.6 20C5.6 15.6 2.3 12.7 2.3 8.9 2.3 5.8 4.7 3.4 7.8 3.4c1.7 0 3.4.8 4.4 2.1 1-1.3 2.7-2.1 4.4-2.1 3.1 0 5.5 2.4 5.5 5.5 0 3.8-3.3 6.7-8.3 11.1L12 21.2Z" />
      </svg>
      <span>{copy.by}</span>
      <a
        className="text-[color:var(--color-accent-bright)] underline-offset-4 transition hover:text-[color:var(--color-text-base)] hover:underline"
        href={AUTHOR_URL}
        rel="noreferrer"
        target="_blank"
      >
        Xinyu YANG
      </a>
    </span>
  );
}
