import { describe, expect, it } from 'vite-plus/test';

import { pxFontSizes } from './build.ts';

describe('pxFontSizes', () => {
  it('rejects a px font size and names its line', () => {
    const css = '.a {\n  color: red;\n  font-size: 14px;\n}\n';
    expect(pxFontSizes(css)).toEqual([
      'packages/ui/styles/primitives.css:3 sets font-size: 14px; use a --type-* token'
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
