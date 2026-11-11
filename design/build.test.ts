import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import { describe, expect, it } from 'vite-plus/test';

import {
  buildTailwindTheme,
  buildTokensCss,
  pxFontSizes,
  tailwindColorName,
  type DesignTokens
} from './build.ts';

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

describe('tailwindColorName', () => {
  it('drops a leading bg- and keeps every other name', () => {
    expect(tailwindColorName('bg-canvas')).toBe('canvas');
    expect(tailwindColorName('text-1')).toBe('text-1');
    expect(tailwindColorName('line-strong')).toBe('line-strong');
    expect(tailwindColorName('hero-stelle')).toBe('hero-stelle');
    expect(tailwindColorName('window-close')).toBe('window-close');
  });
});

describe('buildTailwindTheme', () => {
  const css = buildTailwindTheme(tokens);
  const body = css.slice(css.indexOf('@theme inline {'));
  const declarations = [...body.matchAll(/^  (--[\w*-]+): ([^;]+);$/gm)].map(
    ([, name, value]) => [name, value]
  );
  const names = declarations.map(([name]) => name);

  it('maps each product colour to its token, without the in-game palette', () => {
    const colors = declarations.filter(
      ([name]) => name.startsWith('--color-') && name !== '--color-*'
    );
    const expected = tokens.color.tokens
      .filter((token) => !token.name.startsWith('ingame-'))
      .map((token) => [
        `--color-${tailwindColorName(token.name)}`,
        `var(--${token.name})`
      ]);
    expect(colors).toEqual(expected);
    expect(colors).toContainEqual(['--color-canvas', 'var(--bg-canvas)']);
    expect(names.some((name) => name.includes('ingame'))).toBe(false);
  });

  it('resets the colour and font namespaces before it fills them', () => {
    expect(names[0]).toBe('--color-*');
    const fontReset = names.indexOf('--font-*');
    expect(fontReset).toBeGreaterThan(0);
    expect(
      names.findIndex(
        (name) => name.startsWith('--font-') && name !== '--font-*'
      )
    ).toBeGreaterThan(fontReset);
    expect(names).not.toContain('--text-*');
  });

  it('maps each radius token without its r- prefix', () => {
    const radii = declarations.filter(([name]) => name.startsWith('--radius-'));
    expect(radii).toEqual(
      tokens.radius.tokens.map((token) => [
        `--radius-${token.name.slice(2)}`,
        `var(--${token.name})`
      ])
    );
  });

  it('adds a size-only text utility for every type style', () => {
    const sizes = declarations.filter(([name]) => name.startsWith('--text-'));
    const expected = tokens.type.groups.flatMap((group) =>
      group.styles.map((style) => [
        `--text-${style.name}`,
        `var(--type-${style.name})`
      ])
    );
    expect(sizes).toEqual(expected);
    expect(names.some((name) => name.includes('--line-height'))).toBe(false);
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
