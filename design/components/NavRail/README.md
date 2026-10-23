The installer's 72px icon rail: four pages at the top, then Support and the language toggle at its foot.

Source: `bazaarplusplus-installer/src/layouts/ShellNavRail.tsx` with `.bpp-nav`, `.bpp-rail-item` and `.bpp-nav-footer`.

## Use

- The rail is `bg-sidebar` with a right `line`. Items are 60 × 56px on `r-panel`, with a 22px icon over a 13px `micro` label.
- The pages are Install (Download), History (History), Stream (MonitorPlay) and About (Info). The foot holds Support (opens the Menu beside the rail) and the language toggle ("EN" / "中文").
- Idle items are `text-2` and become `bg-hover` with `text-1` on hover. The active item is `bg-selected` with `text-1` and an `accent` icon.
- Navigation is instant. The page content fades in once over 120ms.

## The consumer provides

The route list and the active route (`aria-current="page"`).
