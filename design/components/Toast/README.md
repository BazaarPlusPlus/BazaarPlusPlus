A transient confirmation in the top-right corner, above modals, with an optional single action.

Source: `bazaarplusplus-installer/src/components/ui/Toast.tsx` (ToastProvider plus the `useToast()` controller) with `.bpp-toast*`. The viewport is a Popover API top-layer element at top 52px, right 16px.

## Use

- Tones: `success`, `info`, `warning` and `error`. Only the icon takes the tone colour; the card is `bg-selected` with `line-strong` and `shadow-float`.
- The card has 8/12px padding, a 16px tone icon and an `icon-sm` ghost close button that hangs 8px into the padding. The first line is 30px high, so icon, message and close share a centre line.
- Keep messages to one short line ("本地数据已删除"). Use at most one action, as an accent text button ("重启"). The action names the next step, so the message doesn't repeat it ("更新已就绪" + 重启, not "更新完成，重启后生效" + 立即重启).
- Toasts enter with opacity and a 4px slide over `--t-base`, and leave over `--t-fast`. They auto-dismiss after `durationMs`.
- Use a toast for the outcome of something the person just did. Use StatusBanner for anything that persists.
- A prompt toast is an offer the person may take or leave, shown at most once: an amber icon, a bold title, one muted line, then a ghost dismiss ("稍后") and a primary `sm` accept button in a right-aligned footer. It is 340px wide with 12/16px padding and has no close button, since the dismiss carries it. It also auto-dismisses, so ignoring it declines.

## The consumer provides

`toast({tone, message, action?, durationMs?, id?})` through `useToast()`; a prompt also passes `title`, `icon` and `dismissLabel`.
