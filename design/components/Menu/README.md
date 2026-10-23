A header or rail popover menu: one-line icon and title rows on a floating layer.

Source: `.bpp-popover`, `.bpp-menu*` in `bundle.css`. The support menu (`bazaarplusplus-installer/src/layouts/ShellSupportMenu.tsx`) is the main user.

## Use

- These are the only floating layers in the shell: `bg-selected`, `line-strong`, `r-panel` and `shadow-float`, entering with opacity and a 0.98 scale over `--t-base`.
- Items have 6 × 8px padding on `r-control`, become `bg-hover` on hover or focus, and use a 16px `text-2` icon beside a `body-medium` (15px) title.
- One line per item. Add a `caption` subtitle in `text-2` only when two items would otherwise read the same; never one that repeats the title ("支持者名单 / 查看名单").
- Placement: `is-right` aligns to the trigger's right edge, `is-beside` opens to the right of the rail and grows upward, and `is-centered` centres under the trigger.
- The minimum width is 232px.

## The consumer provides

The trigger with `aria-expanded`, and menu items as links or buttons.
