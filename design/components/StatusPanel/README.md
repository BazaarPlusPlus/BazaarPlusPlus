The one layout for a region with nothing to show yet: loading, empty, error and not-found. It is a mark, a title, at most one sentence of body and up to two actions.

Source: `bazaarplusplus-installer/src/components/ui/StatusPanel.tsx` and `bazaarplusplus-site/src/shared/components/StatusPanel.tsx`, one spec styled by `.bpp-status-panel*` in `bundle.css`.

## Use

- Marks are 22px Lucide icons: Loader2 spinning in `text-2` (loading), the page's own icon in `text-3` (empty), Alert in `danger` (error) and Compass in `text-3` (not-found).
- The title uses `title` (18px, 600, `text-1`). The body is `body` in `text-2`, at most 44ch. Empty states keep one sentence saying what will fill the space. Errors usually need only the title and a Retry; never a sentence that repeats the button ("请重试").
- Actions are Buttons: one primary that fixes the cause, and optionally one secondary that checks it.
- Frame the panel with `bpp-panel` when it stands alone, and leave the frame off inside another panel.
- `is-compact` is the single-line loading state (16px spinner plus a label), used in place of a panel that is still loading. Label it with the work being done ("正在检测 The Bazaar 与 BazaarPlusPlus"), never just "Loading…".
- Loading and error announce politely (`role="status"`, `aria-busy` while loading).
- No progress bars or pulsing dots: busy is always the Loader2 spinner, as in Button.

## The consumer provides

`kind` (loading, empty, error or not-found), `title`, and optionally `body`, `actions`, `compact` and `framed`.
