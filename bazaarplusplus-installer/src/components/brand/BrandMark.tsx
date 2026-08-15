import clsx from 'clsx';

export function BrandMark({
  className,
  compact = false
}: {
  className?: string;
  compact?: boolean;
}) {
  return (
    <svg
      viewBox="0 0 72 72"
      aria-hidden="true"
      className={clsx('bpp-brand-mark', compact && 'is-compact', className)}
    >
      <defs>
        <linearGradient id="bpp-mark-gold" x1="12" y1="8" x2="60" y2="64">
          <stop offset="0" stopColor="#ffb22f" />
          <stop offset="0.5" stopColor="#dc7912" />
          <stop offset="1" stopColor="#75400d" />
        </linearGradient>
        <linearGradient id="bpp-mark-core" x1="36" y1="27" x2="36" y2="59">
          <stop offset="0" stopColor="#ffe19a" />
          <stop offset="0.58" stopColor="#ffb63c" />
          <stop offset="1" stopColor="#be6710" />
        </linearGradient>
        <filter id="bpp-mark-glow" x="-50%" y="-50%" width="200%" height="200%">
          <feGaussianBlur stdDeviation="2.3" result="blur" />
          <feMerge>
            <feMergeNode in="blur" />
            <feMergeNode in="SourceGraphic" />
          </feMerge>
        </filter>
      </defs>
      <path
        d="M36 5 65 61H7L36 5Z"
        fill="rgba(8,12,14,.92)"
        stroke="url(#bpp-mark-gold)"
        strokeWidth="2.4"
        filter="url(#bpp-mark-glow)"
      />
      <path
        d="m36 15 8.6 27.5L36 36l-8.6 6.5L36 15Z"
        fill="url(#bpp-mark-gold)"
      />
      <path
        d="m14 56 13.4-13.5L36 36l-4.7 20H14Z"
        fill="#b45e0b"
        opacity=".92"
      />
      <path
        d="m58 56-13.4-13.5L36 36l4.7 20H58Z"
        fill="#7d430d"
        opacity=".95"
      />
      <path d="m36 36 15.4 20H20.6L36 36Z" fill="url(#bpp-mark-core)" />
      <path
        d="M36 5 65 61H7L36 5Z"
        fill="none"
        stroke="#f4a020"
        strokeWidth="1"
        opacity=".55"
      />
      <path
        d="m20.6 56 10.7-14 4.7-6 4.7 6 10.7 14"
        fill="none"
        stroke="#ffca5f"
        strokeWidth="1"
        opacity=".7"
      />
    </svg>
  );
}
