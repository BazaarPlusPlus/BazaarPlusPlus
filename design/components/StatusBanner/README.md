An inline banner for page-level status: tone icon, message, optional actions and a collapsible diagnostic.

Source: `bazaarplusplus-installer/src/components/ui/StatusBanner.tsx` with `.bpp-banner*`. `ProblemBanner` (`bazaarplusplus-installer/src/components/ui/ProblemBanner.tsx`) is the single way a page shows a semantic problem. It wraps StatusBanner with a localized message, the problem's diagnostic and an optional Retry (`sm` secondary button).

## Use

- Banners have 12px padding with an 8px icon gap. The first line is `control-sm` (30px) high, so the icon, the first line of the message and any action button share one centre line; further lines wrap below. ConfirmNote inside dialogs is the same banner without actions.
- Tones and icons: `info` (Info, neutral `bg-panel`), `warning` (TriangleAlert on `warning-subtle`), `error` (CircleX on `danger-subtle`) and `success` (CheckCircle2 on `success-subtle`). The border is the tone's `-line` token (`warning-line`, `danger-line`, `success-line`); info keeps `line`.
- Error banners announce as `role="alert"` / assertive, and the others as `status` / polite.
- Actions sit at the right as `sm` buttons. Diagnostics fold under a "查看诊断信息" / "Show diagnostics" summary in a `code-small` (13px) mono well on `bg-canvas`.
- The message is one sentence of cause, plus a step only when the action button can't carry it ("未能读取战绩。请先关闭游戏。" + 重试). It is `text-1` and selectable, so people can copy errors.

## The consumer provides

`tone`, `message`, and optionally `actions`, `diagnostic` + `diagnosticLabel`, or `icon={null}`.
