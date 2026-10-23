The one hero chip on bazaarplusplus.com: a hero-coloured dot, the hero's short code or name, and an optional trailing value such as a win rate.

Source: `bazaarplusplus-site/src/shared/components/HeroBadge.tsx` (Tailwind utilities). The CSS here (`.bpp-hero-badge*`) is hand-written from those utilities.

## Use

- The dot takes the hero's token: `hero-stelle`, `hero-mak`, `hero-jules`, `hero-dooley`, `hero-karnok`, `hero-pygmalien`, `hero-vanessa` or `hero-dragons`. An unknown hero falls back to `hero-unknown` (`text-3`). Dark hero dots such as Pygmalien and Vanessa are faint on the chip fill, so never show a dot without its code or name.
- `label="short"` (default) shows the three-letter code (STE, MAK, JUL, DOO, KAR, PYG, VAN, DRA) and keeps the full name in `sr-only` text and `title`. `label="full"` truncates the name.
- It shares Badge's geometry: `badge-height`, `r-pill`, `small` text.
- `selected` switches to an `accent-subtle` fill with an `accent-line` border, and the trailing value turns `accent`.
- Hero colours are only for the dot. The name is always `text-1`.

## The consumer provides

`hero` (the canonical name), `selected`, `label` and `children` (the trailing value).
