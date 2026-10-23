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
