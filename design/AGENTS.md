# AGENTS.md

The BazaarPlusPlus design system: the one source for design tokens, the shared `bpp-*` component stylesheet, the brand book and writing standard, and the brand mark. Repo-wide rules are in `../AGENTS.md`; the Chinese summary for contributors is `../docs/design.md`.

| File | Owns | Consumers |
|---|---|---|
| `tokens.json` | Every colour, spacing, radius, size, shadow, motion and font-stack value | `bazaarplusplus-installer/src/styles/tokens.css` (generated), which the site copies; the `ingame-*` colours generate the mod's two `InGameTheme.g.cs` files instead |
| `components/bundle.css` | The shared `bpp-*` classes | Imported by `bazaarplusplus-installer/src/styles/index.css`; the site mirrors the same values in Tailwind utilities |
| `README.md`, `writing.md`, `components/*/README.md` | Visual rules, alignment, voice, glossary and component guidelines | Both products |
| `assets/Logos/bpp-mark*.svg` | The brand mark | Installer title bar, About and Install pages |

## Changing the system

- Edit the file that owns the value, run `just design::build`, and commit the regenerated `tokens.css` with it. `just design::check` fails on a stale `tokens.css`, a `var()` in `bundle.css` with no token, or a site hero colour that disagrees with its `hero-*` token.
- A change to tokens or `bundle.css` also passes the installer's and the site's check and test recipes, and regenerates the installer shell goldens (`BPP_UPDATE_GOLDENS=1`).
- The design system is also published as a Design System artifact on claude.ai (https://claude.ai/artifact/4Xv54mp3y68YPc95PWNGVk); `design-system.json` is its index (title, asset records). After a change lands, republish this folder to the artifact under its `project/` paths (everything except `AGENTS.md`, `build.mjs` and `design.just`; images under `assets/` are asset uploads the index names), so the artifact and the repo stay identical. Edits made on the artifact page come back here as a pull request, never the other way round.
- `components/*/preview.html` and `components/Cover/preview.html` are the artifact's live previews; they load `bundle.css` and the tokens, and must stay single-language and free of literal colours.
