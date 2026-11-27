# @bazaarplusplus/ui

React 19 + Tailwind CSS v4 UI kit and *The Bazaar* game components for BazaarPlusPlus.

Live previews and token tables: <https://ui.bazaarplusplus.com>

## Install (GitHub Packages)

GitHub Packages requires authentication for `@bazaarplusplus` even on public packages. Add a `.npmrc` in your project or home directory with a personal access token (or `GITHUB_TOKEN`) that has `read:packages`:

```ini
@bazaarplusplus:registry=https://npm.pkg.github.com
//npm.pkg.github.com/:_authToken=${GITHUB_TOKEN}
```

Then install the package alongside React 19:

```bash
bun add @bazaarplusplus/ui react@^19 react-dom@^19
# or: npm install @bazaarplusplus/ui react@^19 react-dom@^19
```

## Styles

All component styles live inside `@layer components`, so Tailwind utilities always override them cleanly and apps without Tailwind can import the same stylesheets directly.

### With Tailwind CSS v4

```css
@import 'tailwindcss';
@import '@bazaarplusplus/ui/tokens.css';
@import '@bazaarplusplus/ui/theme.css';
@import '@bazaarplusplus/ui/styles.css';
```

Or import only what you use:

- `@bazaarplusplus/ui/tokens.css` — `:root` CSS custom properties
- `@bazaarplusplus/ui/theme.css` — Tailwind CSS v4 `@theme inline` bridge (colors, spacing, radii, typography, shadows, motion)
- `@bazaarplusplus/ui/primitives.css` — primitive component classes (`Button`, `Badge`, `Dialog`, `PageHeader`, `SegmentedControl`, `StatusBanner`, `StatusPanel`)
- `@bazaarplusplus/ui/game.css` — *The Bazaar* game component classes (imports `primitives.css`)
- `@bazaarplusplus/ui/styles.css` — full stylesheet (`primitives.css` + `game.css`)

### Without Tailwind CSS

```css
@import '@bazaarplusplus/ui/tokens.css';
@import '@bazaarplusplus/ui/styles.css';
```

## Usage

### Primitives (`@bazaarplusplus/ui`)

```tsx
import { Badge, Button, Dialog, DialogBody, DialogCard, DialogFooter, PageHeader, SegmentedControl, StatusBanner, StatusPanel } from '@bazaarplusplus/ui';

export function Example() {
  return (
    <PageHeader
      title="Example"
      meta={<Badge tone="accent">Ready</Badge>}
      action={<Button variant="primary">Continue</Button>}
    />
  );
}
```

### Game Components (`@bazaarplusplus/ui/game`)

`@bazaarplusplus/ui/game` is a separate entrypoint so apps that only use primitives never bundle game components or WebGL2 Enchantment FX shaders.

```tsx
import {
  Board,
  CardCatalog,
  CardDetail,
  CardTile,
  createCardCatalogClient,
  DecisionReplay,
  useCardCatalog
} from '@bazaarplusplus/ui/game';

const catalogClient = createCardCatalogClient();

export function BoardExample() {
  return (
    <CardCatalog
      source={{ base: 'https://cdn.bazaarplusplus.com/card-art/' }}
      locale="en"
      client={catalogClient}
    >
      {/* Render CardTile, Board, DayCard, RunReplay, or DecisionReplay */}
    </CardCatalog>
  );
}
```

- Pass `client={createCardCatalogClient({ fetch })}` to isolate caching or provide a custom fetcher.
- Pass `showAllEnchantments` and `onShowAllEnchantmentsChange` to `<CardDetail>` for SSR-safe controlled state instead of shared `localStorage`.
- `DecisionReplay` and `StreamFrames` accept structural replay types (`ReplayVisit`, `ReplayBattle`, `MessageStreamLike`) with zero runtime dependency on internal parser packages.
