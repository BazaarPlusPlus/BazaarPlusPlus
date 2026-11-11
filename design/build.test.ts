import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import { describe, expect, it } from 'vite-plus/test';

import { buildTokensCss, pxFontSizes, type DesignTokens } from './build.ts';

const designDir = path.dirname(fileURLToPath(import.meta.url));
const tokens = JSON.parse(
  fs.readFileSync(path.join(designDir, 'tokens.json'), 'utf8')
) as DesignTokens;

describe('buildTokensCss', () => {
  it('emits one --type-* font size per text style, in scale order', () => {
    const css = buildTokensCss(tokens);
    const emitted = [...css.matchAll(/--type-([\w-]+): (\d+px);/g)].map(
      ([, name, size]) => `${name} ${size}`
    );
    const expected = tokens.type.groups.flatMap((group) =>
      group.styles.map((style) => `${style.name} ${style.fontSize}`)
    );
    expect(emitted).toEqual(expected);
  });
});

describe('pxFontSizes', () => {
  it('rejects a px font size and names its line', () => {
    const css = '.a {\n  color: red;\n  font-size: 14px;\n}\n';
    expect(pxFontSizes(css)).toEqual([
      'design/components/bundle.css:3 sets font-size: 14px; use a --type-* token'
    ]);
  });

  it('accepts a --type-* token', () => {
    expect(pxFontSizes('.a { font-size: var(--type-caption); }')).toEqual([]);
  });

  it('accepts a relative em size', () => {
    expect(pxFontSizes('.mono { font-size: 0.94em; }')).toEqual([]);
  });

  it('ignores a px size inside a comment', () => {
    expect(pxFontSizes('/* font-size: 15px */\n.a {}')).toEqual([]);
  });
});
