# UI kit documentation

The app compiles the shared Web component library and reads the design tokens. Each example and its source come from the same module. Keep Chinese and English controls together.

Run `just ui-docs::refresh-demo` (`bun tools/export-run-demo.ts` then `bun tools/pin-card-art.ts`) when refreshing examples. These authoring tools commit snapshots of the sanitized Run and published Card Art. Production serves them as static assets.

Matching pushes to master deploy this documentation site to production. Manual dispatch selects preview or production.
