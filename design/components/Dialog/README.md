The one modal frame: card, header with a tone icon, body and footer. ConfirmDialog builds every destructive confirmation on it.

Source: `bazaarplusplus-installer/src/components/ui/Dialog.tsx` (Dialog, DialogCard, DialogHeader, DialogBody, DialogFooter), `bazaarplusplus-installer/src/components/ui/ConfirmDialog.tsx` (ConfirmDialog, ConfirmNote, ConfirmTarget) and `.bpp-dialog*` / `.bpp-confirm*`. The site's `bazaarplusplus-site/src/shared/components/DialogShell.tsx` is the same card in Tailwind utilities.

## Use

- The dialog is a native `<dialog>` in the top layer: Escape closes it, focus is trapped, and the backdrop is the `backdrop` token, with no blur. The card enters with opacity and a 0.98 scale.
- Card widths are `sm` 440px, `md` 520px and `lg` 600px, in `bg-panel` with `line-strong`, `r-dialog` and `shadow-float`.
- The card is padded 16px all round. Header: a 36px icon tile (`neutral`, `accent` or `danger`), a `title`-style heading and a ghost close ×, all centred on one line; the close × hangs 8px into the padding so its glyph meets the card's 16px edge. Add a `text-2` subtitle only when it says something the title and body don't.
- The body is one or two sentences: what is deleted, and what is kept. Show a ConfirmNote only when it applies right now (the game is running), not as standing advice.
- A confirmation names its target in a mono `ConfirmTarget`, warns in a `ConfirmNote` (warning or danger) and can require an acknowledgement checkbox (accent-coloured) before the confirm button enables.
- In the footer, Cancel (ghost) sits left of the action (primary, or danger for destructive actions). Once an operation that cannot be cancelled has started, a status line replaces Cancel ("操作已开始，完成前无法取消或关闭此窗口。").

## The consumer provides

`titleId`, `title`, `subtitle`, `tone`, the body `children`, `confirmLabel` / `busyLabel`, `busy`, `onConfirm`, `onClose`, an optional `acknowledge`, and an `activeDismissalPolicy` (blocked, detachable or cancelable).
