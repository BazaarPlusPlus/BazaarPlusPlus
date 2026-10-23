A page's title row: optional leading control, the page title, short meta and right-aligned actions.

Source: `bazaarplusplus-installer/src/components/ui/PageHeader.tsx` and `bazaarplusplus-installer/src/components/ui/PageShell.tsx` (the page column, with 16px gaps between sections), with `.bpp-page*` and `.bpp-section-title`.

## Use

- Use one `display` title per page (24px, 600, −0.01em, single line with an ellipsis), the same style the site uses for its page titles. It is focusable (`data-page-heading`), so route changes can move focus to it.
- Meta is a short `text-3` fact beside the title, on the title's baseline. Actions centre on the row, and a leading back button hangs 8px so its glyph meets the page edge. Actions sit at the far right, usually ghost or secondary buttons.
- Label groups below with `bpp-section-title` (`small`: 14px, 500, `text-3`, sentence case). Never put an uppercase eyebrow above the title.

## The consumer provides

`title`, and optionally `leading` (such as a back button), `meta` and `action`.
