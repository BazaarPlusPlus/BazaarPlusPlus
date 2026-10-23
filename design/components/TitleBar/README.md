The installer's own 44px title bar: the brand mark, the product name and version, header controls, and the window controls on Windows.

Source: `bazaarplusplus-installer/src/layouts/ShellHeader.tsx` with `.bpp-header`, `.bpp-brand*`, `.bpp-header-*` and `.bpp-window-*`.

## Use

- The bar is `bg-sidebar` with a bottom `line`. The brand is the 20px mark (`bpp-mark.svg`, no radius), "BazaarPlusPlus" in `body-strong`, and the version in `caption` (14px) `text-3` tabular figures.
- Header icon controls are `icon-sm` ghost buttons (30px, `text-2`, becoming `bg-hover` with `text-1`).
- macOS uses the overlay title bar: 78px left padding for the traffic lights (14px in full screen), and the whole bar is a drag region except its controls.
- Windows uses a frameless window with 46px window controls at the right. Close hovers `#c42b1c` with white, the platform's own colour.

## The consumer provides

The version, the header actions and the platform (`data-bpp-platform`, `data-bpp-titlebar`).
