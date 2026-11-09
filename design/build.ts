#!/usr/bin/env node
// Builds the CSS custom properties the products read from design/tokens.json,
// and checks that the design system and its consumers agree.
//
//   bun design/build.ts          write the generated tokens.css and copy the brand mark and the covers
//   bun design/build.ts --check  fail if any output is stale or a consumer disagrees
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

/** A per-theme value ({ dark: "#fff" }) or a plain string; "{name}" is an alias. */
export type TokenValue = string | Record<string, string>;

/** One token of design/tokens.json. */
export interface DesignToken {
  name: string;
  value: TokenValue;
  usage: string;
}

/** A token family of design/tokens.json: color, spacing, radius, size, shadow or motion. */
export interface DesignTokenGroup {
  note?: string;
  tokens: DesignToken[];
}

/** A colour theme; the first one is the theme the products ship. */
export interface ColorTheme {
  id: string;
  name: string;
}

/** One text style of a type group. */
export interface TypeStyle {
  name: string;
  fontSize: string;
  lineHeight: number;
  fontWeight: number;
  usage: string;
  sample: string;
  letterSpacing?: string;
}

/** A group of text styles that share one font family. */
export interface TypeGroup {
  name: string;
  family: string;
  styles: TypeStyle[];
}

/** design/tokens.json, as far as this script reads it. */
export interface DesignTokens {
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

/** A token family emitted as custom properties. */
type TokenFamily =
  | 'color'
  | 'spacing'
  | 'radius'
  | 'size'
  | 'shadow'
  | 'motion';

/** A text style as the Android resources read it. */
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
// The cover art the Android App launcher bundles (design/components/Launcher, Cover art).
const COVERS = path.join(designDir, 'assets', 'Covers', 'the-bazaar');
// The installer consumes the brand mark; the site header uses its existing app icon.
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
// The mod's in-game theme: every `ingame-*` colour token, generated as C# for
// the Desktop UI (the Mono host) and the Mobile UI tree the Android host compiles.
// Both read one palette; only their layouts differ.
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
// The Desktop UI's kit sizes: the shared control heights and spacing scale and
// the `ingame-text-*` type sizes, as C# constants (design/components/InGameKit/).
// The Mobile UI sizes itself from TouchMetrics instead (ADR-0018 in the mod).
const INGAME_SIZES = {
  file: 'bazaarplusplus-mod/src/BazaarPlusPlus/Infrastructure/UiTokens/InGameSizes.g.cs',
  namespace: 'BazaarPlusPlus.Infrastructure.UiTokens'
};
const INGAME_SHARED_SIZES = ['control-md', 'control-sm', 'badge-height'];
// The Android App reads the product palette and scales as Android resources and
// the brand mark as a vector drawable; both are generated, never edited.
const ANDROID_RES = 'bazaarplusplus-android/app/src/main/res';
const ANDROID_TOKENS = `${ANDROID_RES}/values/bpp_tokens.xml`;
const ANDROID_MARK = `${ANDROID_RES}/drawable/bpp_mark.xml`;
const markPath = path.join(designDir, 'assets', 'Logos', 'bpp-mark.svg');
// The desktop app icon is also the Android App's launcher icon. The adaptive
// icon is exported by a script and records the icon's sha256, so a changed icon
// without a re-export fails the check.
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
// Core's supporter attribution writes the in-game supporter colours into rich
// text as hex, for the touch host's single label; it cannot read InGameTheme.
const supporterAttributionPath = path.join(
  repoRoot,
  'bazaarplusplus-mod',
  'src',
  'BazaarPlusPlus.Core',
  'Game',
  'Supporters',
  'BPPSupporterAttribution.cs'
);

// Families emitted as custom properties, in output order. `type` contributes
// its font stacks; its styles are documentation for the design system page.
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

// Values are per theme objects ({ dark: "#fff" }) or plain strings; an alias
// "{name}" becomes a var() reference so a change to the target follows.
function cssValue(token: DesignToken, theme: string): string {
  const raw =
    typeof token.value === 'string' ? token.value : token.value[theme];
  const alias = /^\{([A-Za-z0-9][A-Za-z0-9_.-]*)\}$/.exec(raw);
  return alias ? `var(--${alias[1]})` : raw;
}

export function buildTokensCss(tokens: DesignTokens): string {
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
  lines.push('}', '');
  return lines.join('\n');
}

function tokenNames(tokens: DesignTokens): Set<string> {
  const names = new Set<string>();
  for (const family of FAMILIES) {
    for (const token of tokens[family]?.tokens ?? []) names.add(token.name);
  }
  for (const key of Object.keys(tokens.type.families)) names.add(`font-${key}`);
  return names;
}

// Every var() in the shared stylesheet must be a token, or a custom property
// the stylesheet declares itself (such as --tier on outcome pills).
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

// "#rrggbb" or "#rrggbbaa" as the four 0..1 channels Unity's Color takes.
// An unresolvable token arrives as undefined and fails here as "got undefined".
function hexChannels(hex: string | undefined): string[] {
  const match = /^#([0-9a-f]{6})([0-9a-f]{2})?$/.exec(String(hex));
  if (!match)
    throw new Error(`in-game colours must be #rrggbb[aa], got ${hex}`);
  const bytes = [0, 2, 4].map((i) => parseInt(match[1].slice(i, i + 2), 16));
  bytes.push(match[2] ? parseInt(match[2], 16) : 255);
  // Exact byte fractions, so a token matches the 8-bit colour it came from.
  return bytes.map((byte) =>
    byte === 0 ? '0f' : byte === 255 ? '1f' : `${byte}f / 255f`
  );
}

// A CSS colour as Android's #AARRGGBB.
// An unresolvable token arrives as undefined and fails here as "got undefined".
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

// The colours the App launchers use: every product colour, without the
// in-game palette and the hero series.
const appColors = (tokens: DesignTokens): DesignToken[] =>
  tokens.color.tokens.filter(
    (token) =>
      !token.name.startsWith(INGAME_PREFIX) && !token.name.startsWith('hero-')
  );

// The text styles of every type group, as { name, size (px), lineHeight, weight, family }.
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

// The product colours (not ingame-* or hero-*) and the spacing, radius and
// size scales, with CSS px as Android dp.
export function buildAndroidTokens(tokens: DesignTokens): string {
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

// The brand mark's filled paths as an Android vector drawable.
export function buildAndroidMark(svg: string): string {
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

export function buildInGameTheme(
  tokens: DesignTokens,
  namespace: string
): string {
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

export function buildInGameSizes(
  tokens: DesignTokens,
  namespace: string
): string {
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

// The site's hero table owns hero identity; its colours must match the
// hero-* tokens the design system documents.
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

// BPPSupporterAttribution's MutedHex and TierHex repeat ingame-ink-muted and
// ingame-supporter-1..4 as RRGGBB; each must match its token.
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
    {
      file: INGAME_SIZES.file,
      source: buildInGameSizes(tokens, INGAME_SIZES.namespace)
    },
    { file: ANDROID_TOKENS, source: buildAndroidTokens(tokens) },
    {
      file: ANDROID_MARK,
      source: buildAndroidMark(fs.readFileSync(markPath, 'utf8'))
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
  problems.push(...heroMismatches(tokens));
  problems.push(...supporterMismatches(tokens));
  problems.push(...staleLauncherIcon());

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
