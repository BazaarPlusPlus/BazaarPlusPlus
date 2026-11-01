#!/usr/bin/env node
// Builds the CSS custom properties the products read from design/tokens.json,
// and checks that the design system and its consumers agree.
//
//   node design/build.mjs          write the generated tokens.css and copy the brand mark
//   node design/build.mjs --check  fail if any output is stale or a consumer disagrees
import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

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
// The installer consumes the brand mark; the site header uses its existing app icon.
const COPIES = [
  {
    from: path.join(designDir, 'assets', 'Logos', 'bpp-mark.svg'),
    to: 'bazaarplusplus-installer/static/brand/bpp-mark.svg'
  }
];
// The mod's in-game theme: every `ingame-*` colour token, generated as C# for
// the Desktop UI (the Mono host) and the Mobile UI tree both hosts compile.
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
// The Android App reads the product palette and scales as Android resources and
// the brand mark as a vector drawable; both are generated, never edited.
const ANDROID_RES = 'bazaarplusplus-android/app/src/main/res';
const ANDROID_TOKENS = `${ANDROID_RES}/values/bpp_tokens.xml`;
const ANDROID_MARK = `${ANDROID_RES}/drawable/bpp_mark.xml`;
const markPath = path.join(designDir, 'assets', 'Logos', 'bpp-mark.svg');
const heroesPath = path.join(
  repoRoot,
  'bazaarplusplus-site',
  'src',
  'shared',
  'lib',
  'heroes.ts'
);

// Families emitted as custom properties, in output order. `type` contributes
// its font stacks; its styles are documentation for the design system page.
const FAMILIES = ['color', 'spacing', 'radius', 'size', 'shadow', 'motion'];
const FAMILY_TITLES = {
  color: 'Color',
  spacing: 'Spacing',
  radius: 'Radius',
  size: 'Size',
  shadow: 'Shadow',
  motion: 'Motion'
};

function readTokens() {
  return JSON.parse(fs.readFileSync(tokensPath, 'utf8'));
}

// Values are per theme objects ({ dark: "#fff" }) or plain strings; an alias
// "{name}" becomes a var() reference so a change to the target follows.
function cssValue(token, theme) {
  const raw =
    typeof token.value === 'string' ? token.value : token.value[theme];
  const alias = /^\{([A-Za-z0-9][A-Za-z0-9_.-]*)\}$/.exec(raw);
  return alias ? `var(--${alias[1]})` : raw;
}

export function buildTokensCss(tokens) {
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

function tokenNames(tokens) {
  const names = new Set();
  for (const family of FAMILIES) {
    for (const token of tokens[family]?.tokens ?? []) names.add(token.name);
  }
  for (const key of Object.keys(tokens.type.families)) names.add(`font-${key}`);
  return names;
}

// Every var() in the shared stylesheet must be a token, or a custom property
// the stylesheet declares itself (such as --tier on outcome pills).
function undefinedBundleVars(tokens) {
  const css = fs.readFileSync(bundlePath, 'utf8');
  const declared = new Set(
    [...css.matchAll(/(?:^|[;{\s])--([\w-]+)\s*:/g)].map((match) => match[1])
  );
  const known = tokenNames(tokens);
  return [...new Set([...css.matchAll(/var\(--([\w-]+)/g)].map((m) => m[1]))]
    .filter((name) => !known.has(name) && !declared.has(name))
    .sort();
}

function resolveColor(tokens, name, seen = new Set()) {
  const token = tokens.color.tokens.find((entry) => entry.name === name);
  if (!token || seen.has(name)) return undefined;
  seen.add(name);
  const value = cssValue(token, tokens.color.themes[0].id);
  const alias = /^var\(--([\w-]+)\)$/.exec(value);
  return alias ? resolveColor(tokens, alias[1], seen) : value.toLowerCase();
}

function pascalCase(name) {
  return name
    .split('-')
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join('');
}

// "#rrggbb" or "#rrggbbaa" as the four 0..1 channels Unity's Color takes.
function hexChannels(hex) {
  const match = /^#([0-9a-f]{6})([0-9a-f]{2})?$/.exec(hex);
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
function androidColor(value) {
  const hex = /^#([0-9a-f]{6})([0-9a-f]{2})?$/i.exec(value);
  if (hex) return `#${(hex[2] ?? 'ff') + hex[1]}`.toUpperCase();
  const rgba = /^rgba\(\s*(\d+),\s*(\d+),\s*(\d+),\s*([0-9.]+)\s*\)$/.exec(
    value
  );
  if (!rgba)
    throw new Error(`Android colours must be hex or rgba(), got ${value}`);
  const byte = (n) => Number(n).toString(16).padStart(2, '0');
  const alpha = Math.round(Number(rgba[4]) * 255);
  return `#${byte(alpha)}${byte(rgba[1])}${byte(rgba[2])}${byte(rgba[3])}`.toUpperCase();
}

const androidName = (name) => `bpp_${name.replace(/-/g, '_')}`;

// The product colours (not ingame-* or hero-*) and the spacing, radius and
// size scales, with CSS px as Android dp.
export function buildAndroidTokens(tokens) {
  const lines = [
    '<?xml version="1.0" encoding="utf-8"?>',
    '<!-- BazaarPlusPlus design tokens, generated from design/tokens.json by',
    '     `just design::build`. Do not edit: change design/tokens.json and rebuild. -->',
    '<resources>'
  ];
  for (const token of tokens.color.tokens) {
    if (token.name.startsWith(INGAME_PREFIX) || token.name.startsWith('hero-'))
      continue;
    lines.push(
      `  <color name="${androidName(token.name)}">${androidColor(resolveColor(tokens, token.name))}</color>`
    );
  }
  for (const family of ['spacing', 'radius', 'size']) {
    for (const token of tokens[family].tokens) {
      const px = /^(\d+)px$/.exec(cssValue(token, tokens.color.themes[0].id));
      if (!px) continue;
      lines.push(
        `  <dimen name="${androidName(token.name)}">${px[1]}dp</dimen>`
      );
    }
  }
  lines.push('</resources>', '');
  return lines.join('\n');
}

// The brand mark's filled paths as an Android vector drawable.
export function buildAndroidMark(svg) {
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

export function buildInGameTheme(tokens, namespace) {
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

// The site's hero table owns hero identity; its colours must match the
// hero-* tokens the design system documents.
function heroMismatches(tokens) {
  const source = fs.readFileSync(heroesPath, 'utf8');
  const problems = [];
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

function main(argv) {
  const check = argv.includes('--check');
  const tokens = readTokens();
  const css = buildTokensCss(tokens);
  const problems = [];

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
