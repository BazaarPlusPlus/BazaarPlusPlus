The Bazaar's Steam store art, as the store served it on 2026-11-03, for the iOS and Android launchers to bundle (root `docs/adr/0014-bundle-cover-art.md`). `build.mjs` copies it into both Apps; `just design::check` fails on a stale copy. The launcher spec (`components/Launcher/README.md`, Cover art) says which cover shows which image and where it is cropped.

- `the-bazaar/library_hero.jpg`: the library hero (the hunter), 1920 × 620, for a wide cover.
- `the-bazaar/hero_capsule_2x.jpg`: the promotional hero capsule with the logo, 748 × 896, for a portrait cover.
- `the-bazaar/library_capsule_2x.jpg`: the library capsule, 600 × 900, for the iOS App's first-run onboarding.
