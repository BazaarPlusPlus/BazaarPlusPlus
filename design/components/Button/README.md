The one button: primary, secondary, ghost or danger, in md, sm and two icon-only sizes, with a busy state that keeps its width.

Source: `bazaarplusplus-installer/src/components/ui/Button.tsx` with `.bpp-btn*` in `bundle.css`. The site's `bazaarplusplus-site/src/shared/components/Button.tsx` follows the same spec: all four variants and all four sizes. It also renders as an `<a>` when given `href`.

## Use

- **primary**: the one main action in a view (Install, Launch game, Confirm), as an `accent` fill with an `on-accent` 600 label. Use at most one per view.
- **secondary** (default): ordinary actions, as `bg-panel` with a `line-strong` border.
- **ghost**: low-weight and toolbar actions (Refresh, close ×), in `text-2` that becomes `text-1` on `bg-hover`.
- **danger**: destructive actions, as `danger-text` on `danger-subtle` with a `danger-line` border; on hover the fill deepens to `danger-line`. Put them in a confirmation dialog, never as a page's primary action.
- Sizes: `md` is `control-md` (36px) with 12px padding, `body-medium` text and an `icon-md` (16px) icon. `sm` is `control-sm` (30px) with 8px padding, `small` text and an `icon-sm` (14px) icon, for rows, banners and toasts. `icon` is 36×36, and `icon-sm` is 30×30.
- The title-bar icon controls and the toast close button are `icon-sm` ghost buttons. There is no other small square button.

## The consumer provides

- `children` (the label) and an optional leading `icon` (a 24-grid Lucide icon).
- `busy` swaps the icon for a spinning Loader2 and disables the button. `busyLabel` ("正在安装…") replaces the label, and both labels share one grid cell so the width does not jump.
- Icon-only buttons need an `aria-label`.
- For a link that should look like a button, use `buttonClassName({variant, size})` on the `<a>` or router `Link`.

## Don't

- Hand-write button classes on a page.
- Put two primaries side by side.
- Use `danger` for a cancel or close action.
