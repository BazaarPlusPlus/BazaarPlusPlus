import { readdirSync, readFileSync } from 'node:fs';
import { join, relative } from 'node:path';

const uiRoot = import.meta.dirname;
export const primitivesCssPath = join(uiRoot, 'styles', 'primitives.css');
export const gameCssPath = join(uiRoot, 'styles', 'game.css');
export const libraryStylesheets = [primitivesCssPath, gameCssPath] as const;
export const libraryRoot = join(uiRoot, 'src');

export interface UndefinedClass {
  file: string;
  className: string;
}

export function definedClasses(css: string): Set<string> {
  const withoutComments = css.replaceAll(/\/\*[\s\S]*?\*\//g, '');
  const names = new Set<string>();
  for (const match of withoutComments.matchAll(/\.(-?[_a-zA-Z][\w-]*)/g)) {
    const name = match[1];
    if (name !== undefined) names.add(name);
  }
  return names;
}

export function tsxFiles(root: string): string[] {
  return readdirSync(root, { recursive: true, withFileTypes: true })
    .filter((entry) => entry.isFile() && entry.name.endsWith('.tsx'))
    .map((entry) => join(entry.parentPath, entry.name))
    .toSorted();
}

function stringLiterals(source: string): string[] {
  const literals: string[] = [];
  for (const match of source.matchAll(/'([^'\n]*)'|"([^"\n]*)"|`([^`]*)`/g)) {
    // A template's word that holds a ${…} part is dynamic; keep only the
    // static words.
    const text =
      match[1] ?? match[2] ?? match[3]?.replaceAll(/\S*\$\{[^}]*\}\S*/g, ' ');
    if (text !== undefined) literals.push(text);
  }
  return literals;
}

/** Dynamic template tokens such as bpp-btn-${variant} are skipped. */
export function libraryClassTokens(source: string): string[] {
  const tokens: string[] = [];
  const sites = [
    ...balancedAfter(source, /className=\{/g, '{', '}'),
    ...balancedAfter(source, /\bcx\(/g, '(', ')'),
    ...Array.from(source.matchAll(/className=("[^"]*")/g), (m) => m[1] ?? '')
  ];
  for (const site of sites) {
    for (const literal of stringLiterals(site)) {
      tokens.push(...literal.split(/\s+/).filter(Boolean));
    }
  }
  return tokens;
}

function balancedAfter(
  source: string,
  start: RegExp,
  open: string,
  close: string
): string[] {
  const bodies: string[] = [];
  for (const match of source.matchAll(start)) {
    const from = match.index + match[0].length;
    let depth = 1;
    let at = from;
    while (at < source.length && depth > 0) {
      if (source[at] === open) depth += 1;
      else if (source[at] === close) depth -= 1;
      at += 1;
    }
    bodies.push(source.slice(from, at - 1));
  }
  return bodies;
}

/** Only bpp-* tokens belong to the shared stylesheet. Products also use Tailwind utilities. */
export function productBppTokens(source: string): string[] {
  return stringLiterals(source).flatMap((literal) =>
    literal.split(/\s+/).filter((token) => /^bpp-[\w-]*\w$/.test(token))
  );
}

export function undefinedClasses(
  files: readonly string[],
  tokensOf: (source: string) => string[],
  baseDir: string,
  stylesheets: readonly string[] = []
): UndefinedClass[] {
  const defined = definedClasses(
    [...libraryStylesheets, ...stylesheets]
      .map((path) => readFileSync(path, 'utf8'))
      .join('\n')
  );
  const missing: UndefinedClass[] = [];
  for (const file of files) {
    for (const className of new Set(tokensOf(readFileSync(file, 'utf8')))) {
      if (!defined.has(className)) {
        missing.push({ file: relative(baseDir, file), className });
      }
    }
  }
  return missing;
}
