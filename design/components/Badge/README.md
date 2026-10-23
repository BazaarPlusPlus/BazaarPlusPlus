The one 24px label pill. It shows a status as dot + word, and an outcome tier or a neutral fact without a dot.

Source: the installer's `bazaarplusplus-installer/src/components/ui/StatusBadge.tsx` (`.bpp-badge*`) and its `.bpp-outcome` pill, plus the site's `bazaarplusplus-site/src/shared/components/Badge.tsx`. All three converge here on one geometry: `badge-height`, `r-pill`, 8px padding, `small` text (14px, 500).

## Status: dot + word

- Tones: `ok` (success), `warn` (warning), `bad` (danger-text), `busy` (accent, pulsing dot), `accent` (accent, static) and neutral (no tone class: text-2 on bg-hover).
- Each tone is its colour as text on its own `-subtle` fill, and every pairing holds 4.5:1.
- Always say the status in words: 已安装 / Installed, 需要重新安装 / Needs reinstall, Checking. Never show a dot or a colour alone.

## Outcome tiers: no dot

- `.bpp-outcome` with `data-tier`: `diamond` (tier-diamond), `gold` (accent), `silver` (tier-silver), `bronze` (tier-bronze) and `misfortune` (text-2). `data-state="active"` (success) marks a run in progress.
- The pill is the tier colour as text on a 14% tint of itself, so a new tier only sets `--tier`. Write the outcome in words, with numbers in tabular figures.

## Neutral facts

Use the neutral tone with `no-dot` for a version, a season or a count. This replaces the site's separate `Badge`.

## The consumer provides

`tone` (or `data-tier`), `dot`, and the word.
