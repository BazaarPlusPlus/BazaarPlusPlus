The BazaarPlusPlus installer and bazaarplusplus.com share one visual language: a modern desktop tool finished to the standard of Linear, Steam and the Discord client. The brand keeps only the BazaarPlusPlus mark, a chevron over a gem; its single accent is the amber of that gem. Everything else is warm near-black, 1px lines and system type.

Scope: the installer app and the website, plus the colours the mod draws in game (the **In game** section). The OBS stream overlay keeps its own older look and does not use this system.

## Principles

- **One dark theme.** It is used before and after games, often at night. The ground is a warm near-black (`bg-canvas`) and the system does not follow the OS into a light theme.
- **Hierarchy comes from fills, not shadows.** `bg-canvas` → `bg-sidebar` → `bg-panel` → `bg-hover` → `bg-selected` step up in lightness, and panels add only a 1px `line`. `shadow-float` is reserved for floating layers: dialogs, menus, popovers and toasts.
- **No decorative effects.** No gradients, frosted glass, glows, text shadows or vignettes. These made the old installer feel heavy.
- **Amber is emphasis only.** Use `accent` for the primary button, the selected rail icon, key numbers, links and the focus ring. `success`, `warning` and `danger` only carry status and never decorate.
- **Let the layout do the explaining.** Grouping, alignment and order carry meaning, so text does not have to. Give each thing one label and no secondary line that restates it: no subtitle under a clear title, no hint under a clear field, no description under a self-explanatory row.
- **Everything sits on a shared line.** Each element shares an edge, a centre line or a baseline with its neighbour. Nothing is placed by eye.
- **A status is "dot + word".** Colour is the third signal and never carries meaning alone (see Badge).
- **One implementation per component, one value per decision.** The installer and the site share one spec for each component. Every size, gap, radius and colour comes from a token scale; if a value is not on a scale, it is wrong.

## Content fundamentals

The full standard is the **Voice and writing** section: voice, glossary, capitalisation, punctuation, message patterns and number formats. In short:

- **Chinese first, one language per screen.** Every string ships in `zh` and `en`, key for key. Only proper nouns (The Bazaar, BazaarPlusPlus, BepInEx, Steam, OBS) stay in English inside Chinese. The language toggle names the other language: "EN" / "中文".
- **One word per thing.** 战绩 / History, 对局 / run, 回放 / replay, 本地数据 / local data, 游戏目录 / game directory, 插件 / plugin. Never a synonym.
- **Plain, calm and exact.** Buttons are a verb plus an object (选择游戏目录 / Choose game directory). Progress is "正在…" / "-ing…". A failure says what failed and what to do next. No exclamation marks, no emoji, no slang.
- **Sentence case in English, everywhere,** and never ALL CAPS. Never put an uppercase "eyebrow" label above a heading.
- **Say less.** One label per thing. Add a second line only when the first cannot stand alone; a row titled 本地数据 needs a 删除 button, not a description and a 删除本地数据 button.
- **Say what happens and what is kept.** A destructive confirmation names its target, says what it deletes and what survives, and asks for an explicit acknowledgement.

## Colour

- Put page ground on `bg-canvas`. Put the title bar and icon rail on `bg-sidebar`, and content groups on `bg-panel` framed by `line`.
- Give rows, menu items, rail items and ghost buttons `bg-hover` on hover. The selected rail item, the selected segmented option and the fill of floating layers use `bg-selected`.
- Set primary text in `text-1`, secondary text in `text-2`, and meta, keys and hints in `text-3`. All three hold 4.5:1 on every fill from `bg-canvas` to `bg-selected`, so a row keeps its text colours on hover and selection.
- Use `accent` on `on-accent` labels for the one primary action per view. Tinted emphasis is `accent` text on an `accent-subtle` fill; a selected outline is `accent-line`.
- Every tinted fill is its hue at 14% (`-subtle`) and every tinted border is its hue at 32% (`-line`). A status fill pairs with its own text and border: `success` on `success-subtle` with `success-line`, `warning` on `warning-subtle` with `warning-line`, and `danger-text` on `danger-subtle` with `danger-line`. Keep `danger` itself for icons and dots; as small text on its own tint it falls under 4.5:1.
- Run-outcome pills use `tier-bronze`, `tier-silver`, `accent` (gold), `tier-diamond`, `text-2` (misfortune) and `success` (an active run). Each pill is the tier colour as text on a 14% tint of itself.
- Hero colours (`hero-stelle` … `hero-dragons`, `hero-unknown`) are only for dots and chart series. Several are too dark to stand alone on the chip fill, so a dot always sits beside the hero's code or name in `text-1`.
- Never write a colour literal in a component. Reference the token: `var(--accent)`, or Tailwind's `bg-panel`, `text-text-2`, `border-line`, `bg-accent/10`.

## Typography

- Use system UI faces only (`--font-ui`): -apple-system, Segoe UI Variable Text and the PingFang / Microsoft YaHei / Noto Sans SC fallbacks for Chinese. **Fonts are never loaded from a CDN**, so the app renders on mainland networks. On Windows the installer can download Noto Sans SC in the background and moves it to the front of the stack once it is ready.
- One scale serves the installer and the site: five sizes (13, 14, 15, 18, 24) and two working weights (500, 600) over 400. Text is sized for reading Chinese comfortably at desktop distance: nothing in the product is smaller than 13px, and all running text is 15px.

| Style | Size / line / weight | Use |
|---|---|---|
| `display` | 24 / 1.3 / 600, −0.01em | One page title per page |
| `title` | 18 / 1.4 / 600 | Dialog titles, StatusPanel titles, site section headings |
| `body` | 15 / 1.6 / 400 | All running text, in the installer and on the site |
| `body-medium` | 15 / 1.6 / 500 | Buttons, row and menu titles |
| `body-strong` | 15 / 1.6 / 600 | Panel titles, the product name, primary button label |
| `small` | 14 / 1.5 / 500 | Badges, field and section labels, sm buttons |
| `caption` | 14 / 1.5 / 400 | Descriptions, hints, meta |
| `micro` | 13 / 1.2 / 500 | Rail labels only |
| `code`, `code-small` | 14 and 13, mono | Paths, ports, diagnostics |

- Use `--font-mono` only for paths, addresses, ports and diagnostics that must be checked character by character. Inline `.mono` is 0.94em.
- Set numbers in tables and stats with tabular figures (`.tnum`).
- The root stays at 16px so Tailwind's rem scale is standard.

## Spacing, size and radius

- Spacing is a 4px scale (`space-4`, `space-8`, `space-12`, `space-16`, `space-24`) with two half steps (`space-2`, `space-6`) for the insides of controls. Every padding and gap is on it.
- Use `space-6` from an icon to its label, `space-8` between sibling controls, `space-12` inside banners, toasts, notes and rows, and `space-16` inside panels and dialogs and between page sections. The page padding is `space-24` (40px at the foot), and the main column is at most `content-max` (1080px).
- There are two control heights: `control-md` (36px) and `control-sm` (30px). Every button, input, select, title-bar control and close button is one of them. Labels are `badge-height` (24px).
- There are three icon sizes: `icon-sm` (14px) in small controls and badges, `icon-md` (16px) in buttons, banners, toasts and menus, and `icon-lg` (22px) in the rail and StatusPanel marks.
- Radius follows nesting: `r-inner` (4px) inside a control, `r-control` (6px) for anything you click or type into, `r-panel` (8px) for containers, `r-dialog` (12px) for dialogs, and `r-pill` for every 22px label.

## Alignment

- **Edges.** A container has one content edge, its padding: 16px in panels, dialogs and rows, and 12px in banners and toasts. Everything inside starts there, including body text under a dialog header with an icon. A label outside a container (a section title) aligns with the container's outer edge.
- **Start alignment.** Text and controls are start-aligned. Only StatusPanel centres its content.
- **One row height.** Every list row is `row-height` (52px) with its content centred, whether it holds a badge, a 30px button or a value. Rows in neighbouring panels line up.
- **Controls in a line share a centre.** A 24px badge, a 30px button and a line of text in one row are centred on each other. Composite controls are exact multiples: the segmented control is `control-md` (36px) holding `control-sm` (30px) segments.
- **The first line.** When an icon or an action sits beside text that may wrap, it aligns with the first line, not the block. Banners and toasts make their first line `control-sm` high, so icon, text and action share one centre line. A checkbox centres on its label's line.
- **Baselines.** Text of different sizes side by side shares a baseline: a page title and its meta, a product name and its version.
- **Optical edges.** An icon-only ghost button has no visible box, so at a container's edge it pulls out by its inset (8px). Its glyph then lines up with the text and boxed controls on that edge: the dialog close ×, a row's trailing folder button, a toast's close, a back button before a page title.
- **Columns.** Numbers in a column are right-aligned and tabular. Key-value grids share one key column width (148px) across a page.

## Layout

- **Installer shell:** a `titlebar-height` (44px) title bar, a `rail-width` (72px) icon rail and a scrolling main column.
  - On macOS the title bar is the window's overlay title bar. It leaves 78px for the traffic lights (14px in full screen) and the whole bar is a drag region except its controls.
  - On Windows the window is frameless, with in-app window controls on the right (46px wide; close hovers `window-close` with white).
- The rail lists the pages Install, History, Stream and About, with Support and the language toggle at its foot. Each item is a 22px icon over a 13px `micro` label, and the active item is `bg-selected` with an `accent` icon.
- **Site:** a sticky header with the app icon, primary navigation and a language toggle over centred content, built from the same tokens through the Tailwind `@theme` bridge.

## Elevation and borders

- Panels have a 1px `line` border and no shadow. Buttons and floating layers have a 1px `line-strong` border. Inputs and selects have a 1px `line-control` border, because their outline is the only thing that shows the field (3:1 on `bg-canvas` and `bg-panel`).
- Floating layers (menus, popovers, toasts) use `bg-selected` with `line-strong` and `shadow-float`. A dialog card is `bg-panel` + `line-strong` + `shadow-float` + `r-dialog` over the `backdrop` token, with no blur.

## Motion

- Use `--ease` (cubic-bezier(0.2, 0, 0, 1)) with `--t-fast` (100ms) for hover, press and colour changes, and `--t-base` (160ms) for anything entering: routes, dialogs, menus, toasts.
- Route changes are instant, and new content fades in from 0.55 opacity over `--t-base`. Dialogs and menus enter with opacity plus a scale from 0.98, and toasts slide 4px.
- Busy is always the Loader2 spinner (0.8s linear), and a busy badge dot pulses (1.2s). There are no progress bars.
- Respect `prefers-reduced-motion`: every animation and transition collapses to 1ms.

## States and focus

- Show focus as `:focus-visible` with a 2px solid `accent` outline at 2px offset (6.9:1 on `bg-panel`). Inputs instead turn their border `accent` with a 3px `accent-subtle` halo.
- Disabled controls, inputs included, drop to 0.45 opacity. A busy button shows a spinner in place of its icon and keeps the width of its wider label.
- Text selection is `accent-line` with `text-1` text.

## Iconography

- Icons are **Lucide**: `lucide-react` in the installer, and a hand-copied set of Lucide paths in the site's `icons.tsx` (see the Icons group). They are drawn as strokes on a 24px grid at stroke 2, with round caps and joins, in `currentColor`.
- Sizes are `icon-sm`, `icon-md` and `icon-lg` (14, 16 and 22px). Nothing else.
- Idle icons are `text-2`. The active rail icon is `accent`, and status icons take their status colour.
- Status icons are Info, TriangleAlert, CircleX and CheckCircle2 in banners; Loader2 for busy; and Inbox / Compass / Alert for empty, not-found and error StatusPanels.
- No emoji and no illustrated icons. The only marks are the brand mark and the app icon (see Logos).

## Logos

- The **brand mark** is a chevron over a gem: flat, two shapes on a 32-unit grid with 45° edges. The chevron is `text-1` and the gem is `accent`. It is the same silhouette as the app icon and the macOS menu-bar mark, flattened for UI sizes.
  - `bpp-mark.svg` is for dark grounds. Use it at 20px beside "BazaarPlusPlus" in `body-strong` in the title bar and larger on the Install status panel. The site header uses its existing `public/bazaarplusplus-icon.webp` app icon beside the product name.
  - `bpp-mark-on-light.svg` swaps the chevron to `bg-selected` ink for light grounds such as documents and store pages.
  - `bpp-mark-mono-white.svg` is a single white ink for the menu bar and other one-colour places.
  - Give the mark clear space of at least a quarter of its width. Never add a radius, bevel, gradient, glow or outline, and never recolour the gem away from `accent`.
- The **app icon** (`bazaarplusplus-icon.webp`, `app-icon.png`: silver chevron, amber "++" gem and a BAZAAR++ plate on a dark squircle) is the desktop app icon. `favicon.webp` is the site's browser-tab icon.
- If an image is unavailable, set "BazaarPlusPlus" in `body-strong`.

## In game

The mod's panels sit inside The Bazaar, so they speak the game's language rather than the installer's. One visual language serves the desktop and the Android host; only the layouts differ.

- **Native art first.** Fonts, button and frame sprites (`Btn_Rct_*`), trophies, rank badges, cards, boards and portraits are the game's own, borrowed at runtime and never redrawn. BPP draws only what no native sprite covers: grounds, scrims, text colour, gold rules and status colour.
- **Those colours are the `ingame-*` tokens**, generated into `InGameTheme.g.cs` for both hosts (`bazaarplusplus-mod/src/BazaarPlusPlus/Infrastructure/UiTokens/` and `bazaarplusplus-mod/src/BazaarPlusPlus.Android/Ui/`) by `just design::build`. A panel never writes a colour literal; a fraction of a token goes through `InGameTheme.WithAlpha`.
- **Warm brown ground, parchment ink, gold emphasis.** `ingame-backdrop` → `ingame-panel` → `ingame-dialog` are the grounds; `ingame-ink` and `ingame-ink-muted` the text; `ingame-accent` (the game's gold) is the one emphasis colour, as `accent` is in the installer. Status is `ingame-success`, `ingame-failure`, `ingame-warning` and `ingame-pending`, each one value on both hosts.
- **Two layouts.** The desktop lays panels out for a mouse (`HistoryPanelLayout`, `LiveBuildPanelLayout`, `UiTokens/Sizes.cs`); Android lays them out for touch from `TouchChrome` and `TouchMetrics` (48dp targets, 12sp minimum text, ADR-0018 in the mod). A panel looks the same on both and is arranged for its input.
- **Not yet migrated:** the desktop Collection panel still uses its own cool palette in `UiTokens/Colors.cs`; it moves to these tokens next.

## Exceptions

- **Previews are static.** Each component preview is the `bpp-*` markup styled by `components/bundle.css`, the same stylesheet the installer imports; the React components themselves live in `bazaarplusplus-installer/src/components/ui/` and `bazaarplusplus-site/src/shared/components/`.
- **Page-specific styles** (Install status, History run cards and stats, Run detail, Stream) stay in the installer's `src/styles/index.css`, on the same tokens.
- **Outside this system:** the OBS stream overlay (`src-tauri/resources/stream/`, with its Cinzel font and hero badge SVGs), the mod's native art (it is the game's), and the third-party WeChat Pay and Xiaohongshu marks under `static/support/`.
