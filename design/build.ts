#!/usr/bin/env node
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

import { encode, type Raster, stalePlates } from './kit-art.ts';

type TokenValue = string | Record<string, string>;

interface DesignToken {
  name: string;
  value: TokenValue;
  usage: string;
}

interface DesignTokenGroup {
  note?: string;
  tokens: DesignToken[];
}

interface ColorTheme {
  id: string;
  name: string;
}

interface TypeStyle {
  name: string;
  fontSize: string;
  lineHeight: number;
  fontWeight: number;
  usage: string;
  sample: string;
  letterSpacing?: string;
}

interface TypeGroup {
  name: string;
  family: string;
  styles: TypeStyle[];
}

interface DesignTokens {
  name: string;
  version: number;
  color: DesignTokenGroup & { themes: ColorTheme[] };
  type: { families: Record<string, string>; groups: TypeGroup[] };
  spacing: DesignTokenGroup;
  radius: DesignTokenGroup;
  shadow: DesignTokenGroup;
  size: DesignTokenGroup;
  motion: DesignTokenGroup;
}

type TokenFamily =
  | 'color'
  | 'spacing'
  | 'radius'
  | 'size'
  | 'shadow'
  | 'motion';

interface AppTypeStyle {
  name: string;
  size: number;
  lineHeight: number;
  weight: number;
  family: string;
}

const designDir = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.dirname(designDir);
const tokensPath = path.join(designDir, 'tokens.json');
const bundlePath = path.join(designDir, 'components', 'bundle.css');
const tokensCssPath = path.join(
  repoRoot,
  'bazaarplusplus-installer',
  'src',
  'styles',
  'tokens.css'
);
const TAILWIND_THEME = 'design/tailwind-theme.css';
const COVERS = path.join(designDir, 'assets', 'Covers', 'the-bazaar');
const COPIES = [
  {
    from: path.join(designDir, 'assets', 'Logos', 'bpp-mark.svg'),
    to: 'bazaarplusplus-installer/static/brand/bpp-mark.svg'
  },
  // drawable-nodpi: Android must decode them at their own pixel size.
  ...['library_hero.jpg', 'hero_capsule_2x.jpg'].map((name) => ({
    from: path.join(COVERS, name),
    to: `bazaarplusplus-android/app/src/main/res/drawable-nodpi/${name}`
  }))
];
const INGAME_PREFIX = 'ingame-';
const INGAME_TARGETS = [
  {
    file: 'bazaarplusplus-mod/src/BazaarPlusPlus/Infrastructure/UiTokens/InGameTheme.g.cs',
    namespace: 'BazaarPlusPlus.Infrastructure.UiTokens'
  },
  {
    file: 'bazaarplusplus-mod/src/BazaarPlusPlus.Mobile/Ui/InGameTheme.g.cs',
    namespace: 'BazaarPlusPlus.Mobile.Ui'
  }
];
// Mobile UI uses TouchMetrics for sizing (mod ADR-0018).
const INGAME_SIZES = {
  file: 'bazaarplusplus-mod/src/BazaarPlusPlus/Infrastructure/UiTokens/InGameSizes.g.cs',
  namespace: 'BazaarPlusPlus.Infrastructure.UiTokens'
};
const INGAME_SHARED_SIZES = ['control-md', 'control-sm', 'badge-height'];
const ANDROID_RES = 'bazaarplusplus-android/app/src/main/res';
const ANDROID_TOKENS = `${ANDROID_RES}/values/bpp_tokens.xml`;
const ANDROID_MARK = `${ANDROID_RES}/drawable/bpp_mark.xml`;
// Email clients drop SVG, so the server's emails carry the mark as an inline PNG.
const EMAIL_MARK = 'bazaarplusplus-server/src/modules/email-mark.g.ts';
const EMAIL_MARK_SCALE = 3;
const markPath = path.join(designDir, 'assets', 'Logos', 'bpp-mark.svg');
// The exported adaptive icon records the desktop icon's digest so this check can detect a stale export.
const APP_ICON = 'bazaarplusplus-installer/src-tauri/icons/app-icon.png';
const ANDROID_LAUNCHER_ICON = `${ANDROID_RES}/mipmap-anydpi/ic_launcher.xml`;
const heroesPath = path.join(
  repoRoot,
  'bazaarplusplus-site',
  'src',
  'shared',
  'lib',
  'heroes.ts'
);
// Core cannot read InGameTheme. Its touch label needs the supporter colors as inline hex.
const supporterAttributionPath = path.join(
  repoRoot,
  'bazaarplusplus-mod',
  'src',
  'BazaarPlusPlus.Core',
  'Game',
  'Supporters',
  'BPPSupporterAttribution.cs'
);

const FAMILIES: readonly TokenFamily[] = [
  'color',
  'spacing',
  'radius',
  'size',
  'shadow',
  'motion'
];
const FAMILY_TITLES: Record<TokenFamily, string> = {
  color: 'Color',
  spacing: 'Spacing',
  radius: 'Radius',
  size: 'Size',
  shadow: 'Shadow',
  motion: 'Motion'
};

function readTokens(): DesignTokens {
  return JSON.parse(fs.readFileSync(tokensPath, 'utf8')) as DesignTokens;
}

function cssValue(token: DesignToken, theme: string): string {
  const raw =
    typeof token.value === 'string' ? token.value : token.value[theme];
  const alias = /^\{([A-Za-z0-9][A-Za-z0-9_.-]*)\}$/.exec(raw);
  return alias ? `var(--${alias[1]})` : raw;
}

function buildTokensCss(tokens: DesignTokens): string {
  const theme = tokens.color.themes[0].id;
  const lines = [
    '/* BazaarPlusPlus design tokens, generated from design/tokens.json by',
    '   `just design::build`. Do not edit: change design/tokens.json and rebuild. */',
    ':root {',
    `  color-scheme: ${theme};`
  ];
  for (const family of FAMILIES) {
    const list = tokens[family]?.tokens ?? [];
    if (list.length === 0) continue;
    lines.push('', `  /* ${FAMILY_TITLES[family]} */`);
    for (const token of list) {
      if (token.name.startsWith(INGAME_PREFIX)) continue;
      lines.push(`  --${token.name}: ${cssValue(token, theme)};`);
    }
  }
  lines.push('', '  /* Type */');
  for (const [key, stack] of Object.entries(tokens.type.families)) {
    lines.push(`  --font-${key}: ${stack};`);
  }
  for (const style of typeStyles(tokens)) {
    lines.push(`  --type-${style.name}: ${style.size}px;`);
  }
  lines.push('}', '');
  return lines.join('\n');
}

// Strip bg- so Tailwind exposes bg-canvas.
function tailwindColorName(token: string): string {
  return token.startsWith('bg-') ? token.slice('bg-'.length) : token;
}

// Tailwind inline utilities resolve :root tokens. Keep Tailwind's default text sizes alongside the generated styles.
function buildTailwindTheme(tokens: DesignTokens): string {
  const lines = [
    '/* BazaarPlusPlus Tailwind bridge, generated from design/tokens.json by',
    '   `just design::build`. Do not edit: change design/tokens.json and rebuild.',
    '   Utilities read the :root tokens of tokens.css: bg-panel, text-text-2,',
    '   border-line, rounded-control, text-body. */',
    '@theme inline {',
    '  --color-*: initial;'
  ];
  for (const token of tokens.color.tokens) {
    if (token.name.startsWith(INGAME_PREFIX)) continue;
    lines.push(
      `  --color-${tailwindColorName(token.name)}: var(--${token.name});`
    );
  }
  lines.push('');
  for (const token of tokens.radius.tokens) {
    if (!token.name.startsWith('r-')) {
      throw new Error(`radius token ${token.name} needs the r- prefix`);
    }
    lines.push(
      `  --radius-${token.name.slice('r-'.length)}: var(--${token.name});`
    );
  }
  lines.push(
    '',
    '  --font-*: initial;',
    '  --font-sans: var(--font-ui);',
    '  --font-mono: var(--font-mono);',
    ''
  );
  for (const style of typeStyles(tokens)) {
    lines.push(`  --text-${style.name}: var(--type-${style.name});`);
  }
  lines.push('}', '');
  return lines.join('\n');
}

function tokenNames(tokens: DesignTokens): Set<string> {
  const names = new Set<string>();
  for (const family of FAMILIES) {
    for (const token of tokens[family]?.tokens ?? []) names.add(token.name);
  }
  for (const key of Object.keys(tokens.type.families)) names.add(`font-${key}`);
  for (const style of typeStyles(tokens)) names.add(`type-${style.name}`);
  return names;
}

function undefinedBundleVars(tokens: DesignTokens): string[] {
  const css = fs.readFileSync(bundlePath, 'utf8');
  const declared = new Set(
    [...css.matchAll(/(?:^|[;{\s])--([\w-]+)\s*:/g)].map((match) => match[1])
  );
  const known = tokenNames(tokens);
  return [...new Set([...css.matchAll(/var\(--([\w-]+)/g)].map((m) => m[1]))]
    .filter((name) => !known.has(name) && !declared.has(name))
    .sort();
}

export function pxFontSizes(css: string): string[] {
  const problems: string[] = [];
  const text = css.replace(/\/\*[\s\S]*?\*\//g, (comment) =>
    comment.replace(/[^\n]/g, ' ')
  );
  text.split('\n').forEach((line, index) => {
    const value = /(?:^|[;{\s])font-size\s*:\s*([^;}]*)/.exec(line)?.[1];
    if (value && /\d(?:\.\d+)?px\b/.test(value)) {
      problems.push(
        `design/components/bundle.css:${index + 1} sets font-size: ${value.trim()}; use a --type-* token`
      );
    }
  });
  return problems;
}

function resolveColor(
  tokens: DesignTokens,
  name: string,
  seen: Set<string> = new Set()
): string | undefined {
  const token = tokens.color.tokens.find((entry) => entry.name === name);
  if (!token || seen.has(name)) return undefined;
  seen.add(name);
  const value = cssValue(token, tokens.color.themes[0].id);
  const alias = /^var\(--([\w-]+)\)$/.exec(value);
  return alias ? resolveColor(tokens, alias[1], seen) : value.toLowerCase();
}

function pascalCase(name: string): string {
  return name
    .split('-')
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join('');
}

function hexChannels(hex: string | undefined): string[] {
  const match = /^#([0-9a-f]{6})([0-9a-f]{2})?$/.exec(String(hex));
  if (!match)
    throw new Error(`in-game colours must be #rrggbb[aa], got ${hex}`);
  const bytes = [0, 2, 4].map((i) => parseInt(match[1].slice(i, i + 2), 16));
  bytes.push(match[2] ? parseInt(match[2], 16) : 255);
  return bytes.map((byte) =>
    byte === 0 ? '0f' : byte === 255 ? '1f' : `${byte}f / 255f`
  );
}

function androidColor(value: string | undefined): string {
  const hex = /^#([0-9a-f]{6})([0-9a-f]{2})?$/i.exec(String(value));
  if (hex) return `#${(hex[2] ?? 'ff') + hex[1]}`.toUpperCase();
  const rgba = /^rgba\(\s*(\d+),\s*(\d+),\s*(\d+),\s*([0-9.]+)\s*\)$/.exec(
    String(value)
  );
  if (!rgba)
    throw new Error(`Android colours must be hex or rgba(), got ${value}`);
  const byte = (n: number | string): string =>
    Number(n).toString(16).padStart(2, '0');
  const alpha = Math.round(Number(rgba[4]) * 255);
  return `#${byte(alpha)}${byte(rgba[1])}${byte(rgba[2])}${byte(rgba[3])}`.toUpperCase();
}

const androidName = (name: string): string => `bpp_${name.replace(/-/g, '_')}`;

const appColors = (tokens: DesignTokens): DesignToken[] =>
  tokens.color.tokens.filter(
    (token) =>
      !token.name.startsWith(INGAME_PREFIX) && !token.name.startsWith('hero-')
  );

function typeStyles(tokens: DesignTokens): AppTypeStyle[] {
  return tokens.type.groups.flatMap((group) =>
    group.styles.map((style) => {
      const size = /^(\d+)px$/.exec(style.fontSize);
      if (!size) throw new Error(`type style ${style.name} needs a px size`);
      return {
        name: style.name,
        size: Number(size[1]),
        lineHeight: style.lineHeight,
        weight: style.fontWeight,
        family: group.family
      };
    })
  );
}

function buildAndroidTokens(tokens: DesignTokens): string {
  const lines = [
    '<?xml version="1.0" encoding="utf-8"?>',
    '<!-- BazaarPlusPlus design tokens, generated from design/tokens.json by',
    '     `just design::build`. Do not edit: change design/tokens.json and rebuild. -->',
    '<resources>'
  ];
  for (const token of appColors(tokens)) {
    lines.push(
      `  <color name="${androidName(token.name)}">${androidColor(resolveColor(tokens, token.name))}</color>`
    );
  }
  for (const family of ['spacing', 'radius', 'size'] as const) {
    for (const token of tokens[family].tokens) {
      if (token.name.startsWith(INGAME_PREFIX)) continue;
      const px = /^(\d+)px$/.exec(cssValue(token, tokens.color.themes[0].id));
      if (!px) continue;
      lines.push(
        `  <dimen name="${androidName(token.name)}">${px[1]}dp</dimen>`
      );
    }
  }
  // Type sizes in sp, so they follow the system font scale.
  for (const style of typeStyles(tokens)) {
    lines.push(
      `  <dimen name="${androidName(`type-${style.name}`)}">${style.size}sp</dimen>`
    );
  }
  lines.push('</resources>', '');
  return lines.join('\n');
}

function buildAndroidMark(svg: string): string {
  const box = /viewBox="0 0 (\d+) (\d+)"/.exec(svg);
  const paths = [
    ...svg.matchAll(/<path fill="(#[0-9a-f]{6})" d="([^"]+)"\/>/gi)
  ];
  if (!box || paths.length === 0)
    throw new Error('bpp-mark.svg is not the flat viewBox-and-paths shape');
  return [
    '<?xml version="1.0" encoding="utf-8"?>',
    '<!-- The BazaarPlusPlus brand mark, generated from design/assets/Logos/bpp-mark.svg',
    '     by `just design::build`. Do not edit. -->',
    '<vector xmlns:android="http://schemas.android.com/apk/res/android"',
    `    android:width="${box[1]}dp"`,
    `    android:height="${box[2]}dp"`,
    `    android:viewportWidth="${box[1]}"`,
    `    android:viewportHeight="${box[2]}">`,
    ...paths.map(
      ([, fill, d]) =>
        `  <path android:fillColor="${androidColor(fill)}" android:pathData="${d}" />`
    ),
    '</vector>',
    ''
  ].join('\n');
}

/** Rasterises the mark's straight-edged paths, 8x8 samples per pixel, painted in order. */
function rasterizeMark(svg: string, scale: number): Raster {
  const box = /viewBox="0 0 (\d+) (\d+)"/.exec(svg);
  const paths = [
    ...svg.matchAll(/<path fill="#([0-9a-f]{6})" d="([^"]+)"\/>/gi)
  ];
  if (!box || paths.length === 0)
    throw new Error('bpp-mark.svg is not the flat viewBox-and-paths shape');
  const width = Number(box[1]) * scale;
  const height = Number(box[2]) * scale;
  const shapes = paths.map(([, hex, d]) => {
    if (!/^M[\d.\sLZ]+Z$/.test(d.trim()))
      throw new Error(`bpp-mark.svg path is not M/L/Z only: ${d}`);
    const numbers = (d.match(/[\d.]+/g) ?? []).map(Number);
    const points: [number, number][] = [];
    for (let i = 0; i + 1 < numbers.length; i += 2)
      points.push([numbers[i] * scale, numbers[i + 1] * scale]);
    const rgb = [0, 2, 4].map(
      (at) => parseInt(hex.slice(at, at + 2), 16) / 255
    );
    return { points, rgb };
  });
  const inside = (points: [number, number][], x: number, y: number) => {
    let hit = false;
    for (let i = 0, j = points.length - 1; i < points.length; j = i++) {
      const [xi, yi] = points[i];
      const [xj, yj] = points[j];
      if (yi > y !== yj > y && x < ((xj - xi) * (y - yi)) / (yj - yi) + xi)
        hit = !hit;
    }
    return hit;
  };
  const SAMPLES = 8;
  const data = new Float32Array(width * height * 4);
  for (let py = 0; py < height; py++) {
    for (let px = 0; px < width; px++) {
      let [r, g, b, a] = [0, 0, 0, 0];
      for (const shape of shapes) {
        let covered = 0;
        for (let sy = 0; sy < SAMPLES; sy++)
          for (let sx = 0; sx < SAMPLES; sx++)
            if (
              inside(
                shape.points,
                px + (sx + 0.5) / SAMPLES,
                py + (sy + 0.5) / SAMPLES
              )
            )
              covered++;
        const coverage = covered / (SAMPLES * SAMPLES);
        if (coverage === 0) continue;
        const out = coverage + a * (1 - coverage);
        const mix = (top: number, under: number) =>
          (top * coverage + under * a * (1 - coverage)) / out;
        [r, g, b, a] = [
          mix(shape.rgb[0], r),
          mix(shape.rgb[1], g),
          mix(shape.rgb[2], b),
          out
        ];
      }
      data.set([r, g, b, a], (py * width + px) * 4);
    }
  }
  return { width, height, data };
}

function buildEmailMark(svg: string): string {
  const raster = rasterizeMark(svg, EMAIL_MARK_SCALE);
  return [
    '// The BazaarPlusPlus brand mark as a PNG for emails, generated from',
    '// design/assets/Logos/bpp-mark.svg by `just design::build`. Do not edit.',
    `export const BPP_MARK_PNG_SIZE = ${raster.width};`,
    `export const BPP_MARK_PNG_BASE64 =`,
    `  '${encode(raster).toString('base64')}';`,
    ''
  ].join('\n');
}

function buildInGameTheme(tokens: DesignTokens, namespace: string): string {
  const lines = [
    '// <auto-generated>',
    '// BazaarPlusPlus in-game theme, generated from design/tokens.json by',
    '// `just design::build`. Do not edit: change the ingame-* tokens and rebuild.',
    '// </auto-generated>',
    '#nullable enable',
    'using UnityEngine;',
    '',
    `namespace ${namespace};`,
    '',
    '/// <summary>The colours BPP draws in game, shared by the Desktop UI and the Mobile UI.</summary>',
    'internal static class InGameTheme',
    '{'
  ];
  const list = tokens.color.tokens.filter((token) =>
    token.name.startsWith(INGAME_PREFIX)
  );
  list.forEach((token, index) => {
    const hex = resolveColor(tokens, token.name);
    const [r, g, b, a] = hexChannels(hex);
    if (index > 0) lines.push('');
    lines.push(
      `    /// <summary>${token.usage.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')}</summary>`,
      `    public static readonly Color ${pascalCase(token.name.slice(INGAME_PREFIX.length))} = new(${r}, ${g}, ${b}, ${a});`
    );
  });
  lines.push(
    '',
    '    /// <summary>The same colour at another opacity, for rules and tints drawn at a fraction.</summary>',
    '    public static Color WithAlpha(Color color, float alpha) =>',
    '        new(color.r, color.g, color.b, alpha);',
    '}',
    ''
  );
  return lines.join('\n');
}

function buildInGameSizes(tokens: DesignTokens, namespace: string): string {
  const lines = [
    '// <auto-generated>',
    '// BazaarPlusPlus in-game sizes, generated from design/tokens.json by',
    '// `just design::build`. Do not edit: change the tokens and rebuild.',
    '// </auto-generated>',
    '#nullable enable',
    '',
    `namespace ${namespace};`,
    '',
    '/// <summary>The sizes the Desktop UI kit draws with, in canvas units.</summary>',
    'internal static class InGameSizes',
    '{'
  ];
  const theme = tokens.color.themes[0].id;
  const list = [
    ...tokens.size.tokens.filter(
      (token) =>
        INGAME_SHARED_SIZES.includes(token.name) ||
        token.name.startsWith(INGAME_PREFIX)
    ),
    ...tokens.spacing.tokens
  ];
  list.forEach((token, index) => {
    const px = /^(\d+)px$/.exec(cssValue(token, theme));
    if (!px) throw new Error(`${token.name} is not a px size`);
    const name = token.name.startsWith(INGAME_PREFIX)
      ? token.name.slice(INGAME_PREFIX.length)
      : token.name;
    if (index > 0) lines.push('');
    lines.push(
      `    /// <summary>${token.usage.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')}</summary>`,
      `    public const float ${pascalCase(name)} = ${px[1]}f;`
    );
  });
  lines.push('}', '');
  return lines.join('\n');
}

function heroMismatches(tokens: DesignTokens): string[] {
  const source = fs.readFileSync(heroesPath, 'utf8');
  const problems: string[] = [];
  for (const match of source.matchAll(
    /(\w+): \{ shortLabel: '[A-Z]+', color: '(#[0-9a-fA-F]{6})' \}/g
  )) {
    const [, hero, color] = match;
    const name = `hero-${hero === 'TheDragons' ? 'dragons' : hero.toLowerCase()}`;
    const expected = resolveColor(tokens, name);
    if (expected !== color.toLowerCase()) {
      problems.push(`${name} is ${expected} but heroes.ts has ${color}`);
    }
  }
  const fallback = /FALLBACK_HERO_COLOR = '(#[0-9a-fA-F]{6})'/.exec(
    source
  )?.[1];
  const unknown = resolveColor(tokens, 'hero-unknown');
  if (fallback?.toLowerCase() !== unknown) {
    problems.push(
      `hero-unknown is ${unknown} but FALLBACK_HERO_COLOR is ${fallback}`
    );
  }
  return problems;
}

function supporterMismatches(tokens: DesignTokens): string[] {
  const source = fs.readFileSync(supporterAttributionPath, 'utf8');
  const file = 'BPPSupporterAttribution.cs';
  const found = new Map<string, string | undefined>([
    ['ingame-ink-muted', /MutedHex = "([0-9A-Fa-f]{6})"/.exec(source)?.[1]]
  ]);
  const tiers = /TierHex\(int tier\) =>([\s\S]*?)\};/.exec(source)?.[1] ?? '';
  for (const [, arm, hex] of tiers.matchAll(
    /(>= 4|\d|_) => "([0-9A-Fa-f]{6})"/g
  )) {
    const tier = arm === '>= 4' ? '4' : arm === '_' ? '1' : arm;
    found.set(`ingame-supporter-${tier}`, hex);
  }
  const problems: string[] = [];
  for (const name of [
    'ingame-ink-muted',
    'ingame-supporter-1',
    'ingame-supporter-2',
    'ingame-supporter-3',
    'ingame-supporter-4'
  ]) {
    const expected = resolveColor(tokens, name)?.slice(1);
    const actual = found.get(name)?.toLowerCase();
    if (actual !== expected) {
      problems.push(
        `${name} is #${expected} but ${file} has ${actual ?? 'none'}`
      );
    }
  }
  return problems;
}

function staleLauncherIcon(): string[] {
  const sha = crypto
    .createHash('sha256')
    .update(fs.readFileSync(path.join(repoRoot, APP_ICON)))
    .digest('hex');
  const xml = path.join(repoRoot, ANDROID_LAUNCHER_ICON);
  const recorded = fs.existsSync(xml)
    ? /sha256 ([0-9a-f]{64})/.exec(fs.readFileSync(xml, 'utf8'))?.[1]
    : undefined;
  return recorded === sha
    ? []
    : [
        `${ANDROID_LAUNCHER_ICON} is stale for ${APP_ICON}; run \`just android::launcher-icon\``
      ];
}

function main(argv: readonly string[]): void {
  const check = argv.includes('--check');
  const tokens = readTokens();
  const css = buildTokensCss(tokens);
  const problems: string[] = [];

  const current = fs.existsSync(tokensCssPath)
    ? fs.readFileSync(tokensCssPath, 'utf8')
    : '';
  if (current !== css) {
    if (check) {
      problems.push(
        'bazaarplusplus-installer/src/styles/tokens.css is stale; run `just design::build`'
      );
    } else {
      fs.writeFileSync(tokensCssPath, css);
      console.log('Wrote bazaarplusplus-installer/src/styles/tokens.css');
    }
  }

  for (const target of INGAME_TARGETS) {
    const file = path.join(repoRoot, target.file);
    const source = buildInGameTheme(tokens, target.namespace);
    const existing = fs.existsSync(file) ? fs.readFileSync(file, 'utf8') : '';
    if (existing === source) continue;
    if (check) {
      problems.push(`${target.file} is stale; run \`just design::build\``);
    } else {
      fs.writeFileSync(file, source);
      console.log(`Wrote ${target.file}`);
    }
  }

  const generated = [
    { file: TAILWIND_THEME, source: buildTailwindTheme(tokens) },
    {
      file: INGAME_SIZES.file,
      source: buildInGameSizes(tokens, INGAME_SIZES.namespace)
    },
    { file: ANDROID_TOKENS, source: buildAndroidTokens(tokens) },
    {
      file: ANDROID_MARK,
      source: buildAndroidMark(fs.readFileSync(markPath, 'utf8'))
    },
    {
      file: EMAIL_MARK,
      source: buildEmailMark(fs.readFileSync(markPath, 'utf8'))
    }
  ];
  for (const target of generated) {
    const file = path.join(repoRoot, target.file);
    const existing = fs.existsSync(file) ? fs.readFileSync(file, 'utf8') : '';
    if (existing === target.source) continue;
    if (check) {
      problems.push(`${target.file} is stale; run \`just design::build\``);
    } else {
      fs.mkdirSync(path.dirname(file), { recursive: true });
      fs.writeFileSync(file, target.source);
      console.log(`Wrote ${target.file}`);
    }
  }

  for (const copy of COPIES) {
    const target = path.join(repoRoot, copy.to);
    const source = fs.readFileSync(copy.from);
    const existing = fs.existsSync(target) ? fs.readFileSync(target) : null;
    if (existing && existing.equals(source)) continue;
    if (check) {
      problems.push(`${copy.to} is stale; run \`just design::build\``);
    } else {
      fs.mkdirSync(path.dirname(target), { recursive: true });
      fs.writeFileSync(target, source);
      console.log(`Wrote ${copy.to}`);
    }
  }

  const missing = undefinedBundleVars(tokens);
  if (missing.length > 0) {
    problems.push(
      `design/components/bundle.css reads undefined tokens: ${missing.join(', ')}`
    );
  }
  problems.push(...pxFontSizes(fs.readFileSync(bundlePath, 'utf8')));
  problems.push(...heroMismatches(tokens));
  problems.push(...supporterMismatches(tokens));
  problems.push(...staleLauncherIcon());
  problems.push(
    ...stalePlates().map(
      (file) => `${file} is stale; run \`just design::kit-art\``
    )
  );

  if (problems.length > 0) {
    for (const problem of problems) console.error(problem);
    process.exitCode = 1;
  } else if (check) {
    console.log('Design tokens and their consumers agree.');
  }
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  main(process.argv.slice(2));
}
