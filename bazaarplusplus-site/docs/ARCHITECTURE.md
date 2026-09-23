# BazaarPlusPlus Site Architecture

The code is the structural source of truth. This document records intended ownership and cross-module contracts that are costly to infer from individual files.

## Ownership

| Concern | Owner | Boundary |
| --- | --- | --- |
| URL and browser history | `src/app/router.ts` | Owns routes, aliases, locale and Analysis Scope parsing, canonical hrefs, click eligibility, and push/replace/popstate behavior. |
| Page composition | `src/app/App.tsx` and `src/app/route-pages.tsx` | Render the resolved location; `/heroes` stays lazy. Runtime data enters through React Query. |
| Hero Metrics Dataset ingestion | `src/features/heroes/hero-metrics-dataset.ts` | Owns transport, retries, decoding, compatibility, Dataset Coverage, and semantic loading progress. |
| Hero Analysis | `src/features/heroes/hero-analysis.ts` | Pure, React-free policy for scope selection, merging, ranking, trends, matchups, focus fallback, and selected-window coverage. |
| Hero Analysis presentation | `src/features/heroes/HeroOverviewDashboard.tsx`, `HeroTrendPanel.tsx`, and `HeroRankingTable.tsx` | The dashboard composes validated analysis and shares hero focus. The trend module owns SVG geometry and chart interaction; the ranking module owns its columns and sorting policy. |
| Installer resolution | `src/features/download/installer.ts` | Owns Platform Release Manifest transport and primary-download decoding, one manifest per platform. The workspace `release/downloads.ts` owns release origin, platform keys, manifest paths and the mainland mirror decoder `decodeMainlandDownloadUrl`; the download page consumes the resolved per-platform model. |
| Visual system | `src/styles/tokens.css` and `src/shared/components/` | `tokens.css` holds the unified visual tokens (names shared with the installer) and their Tailwind `@theme` bridge; it is the only source of color, font, radius, and motion values. Features compose `Button`, `Badge`, `StatusPanel`, `PageLayout`, `HeroBadge`, `SegmentedControl`, and `DialogShell` instead of restating their classes. |
| WeChat Pay QR artwork | `../bazaarplusplus-installer/static/support/wechat-pay.svg` | The installer owns the SVG; `src/features/support/SupportPage.tsx` imports it as raw markup so both projects display the same payment code. |

The metrics, installer, and SPA location seams each have production and in-memory test adapters. Presentation modules consume their resolved models; payload decoding stays in ingestion and browser-history handling stays in the router.

Installer resolution reads each platform's Platform Release Manifest at `latest/<platformKey>.json` (paths from `platformManifestPath` in workspace `release/downloads.ts`) rather than reconstructing primary filenames, so each card carries its own version and the platforms may differ. Primary URLs must belong to the expected origin, that manifest's version and its platform; a platform whose manifest is missing or invalid degrades only its own card, and the GitHub fallback appears only when no platform loads. The mainland mirror address is the manifest's `downloads[platform].mainlandUrl`, read through `decodeMainlandDownloadUrl` in workspace `release/downloads.ts`, the same decoder the desktop installer uses; a manifest without it disables only the mirror entry. Writer and consumer share the fixtures under `release/fixtures/latest/` at the workspace root; publish a complete manifest before deploying a website that requires this contract.

## URL Contract

- `src/app/router.ts` is the route catalog for App routing, navigation, and page-title keys. Cloudflare supplies SPA fallback; unknown paths still resolve to the localized not-found page.
- Default values—Chinese, `1d`, and `all`—omit `lang`, `w`, and `s`; non-default values serialize explicitly.
- Hero Analysis query values, including retired keys, canonicalize at the SPA location boundary independently of page data loading.
- Analysis Scope changes and alias canonicalization replace history. Internal navigation pushes. Locale links preserve the current path, scope, and hash.

## Hero Metrics Dataset

- The HTTP adapter returns unknown JSON or a structured transport failure; ingestion is the only decoder.
- One snapshot contains 1–7 contiguous daily entries. Its inclusive `window.end`, rather than the wall clock, governs window selection and freshness.
- Additive fields and non-canonical heroes are compatible. An invalid envelope fails the page; an invalid daily entry degrades Dataset Coverage under `CONTEXT.md` semantics.
- Snapshots store `legend` and `non_legend`; analysis derives `all` by additive merge.
