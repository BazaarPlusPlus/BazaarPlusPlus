// Keeps the projects on Vite+ (ADR 0017) on one version. Root package.json
// `vite-plus` owns it; `just vite-plus-sync` copies it into the other
// projects and into every `vite` alias, which `vp` requires to name the same
// core version. Dependabot does not update `npm:` aliases, so
// .github/workflows/vite-plus-sync.yml runs this on its PRs.
//
// Usage: bun scripts/vite-plus.ts [checkout] [bun install flags...]
// The checkout defaults to this repository; the workflow passes the PR head.
import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';

const repository = path.resolve(import.meta.dirname, '..');

/** The directories, relative to the root, whose package.json uses Vite+. */
export const VITE_PLUS_PROJECTS = [
  '.',
  'bazaarplusplus-site',
  'bazaarplusplus-installer'
];

const CORE = 'npm:@voidzero-dev/vite-plus-core@';

interface Manifest {
  devDependencies?: Record<string, string>;
  overrides?: Record<string, unknown>;
}

function manifestPath(checkout: string, project: string): string {
  return path.join(checkout, project, 'package.json');
}

function readManifest(checkout: string, project: string): Manifest {
  return JSON.parse(fs.readFileSync(manifestPath(checkout, project), 'utf8'));
}

/** The `vite-plus` version in root package.json. */
export function ownerVersion(checkout = repository): string {
  const version = readManifest(checkout, '.').devDependencies?.['vite-plus'];
  if (!version) throw new Error('root package.json has no vite-plus');
  return version;
}

/** Each pin that differs from the owner version, as `<file> <field>: <value>`. */
export function misalignments(checkout = repository): string[] {
  const version = ownerVersion(checkout);
  const found: string[] = [];
  for (const project of VITE_PLUS_PROJECTS) {
    const manifest = readManifest(checkout, project);
    const file = path.posix.join(project, 'package.json');
    const pins: [string, unknown, string][] = [
      [
        'devDependencies.vite-plus',
        manifest.devDependencies?.['vite-plus'],
        version
      ],
      ['devDependencies.vite', manifest.devDependencies?.vite, CORE + version],
      ['overrides.vite', manifest.overrides?.vite, CORE + version]
    ];
    for (const [field, actual, expected] of pins)
      if (actual !== expected)
        found.push(`${file} ${field}: ${String(actual)}`);
  }
  return found;
}

// Rewrites the version text in place so the file keeps its formatting.
function pinText(text: string, version: string): string {
  return text
    .replace(/("vite-plus":\s*")[^"]*"/, `$1${version}"`)
    .replaceAll(
      /npm:@voidzero-dev\/vite-plus-core@[^"]*/g,
      `${CORE}${version}`
    );
}

/**
 * Pins every project to the owner version and refreshes the lockfile of each
 * project whose package.json changed. Returns those projects.
 */
export function sync(checkout = repository, installFlags: string[] = []) {
  const version = ownerVersion(checkout);
  const changed: string[] = [];
  for (const project of VITE_PLUS_PROJECTS) {
    const file = manifestPath(checkout, project);
    const before = fs.readFileSync(file, 'utf8');
    const after = pinText(before, version);
    if (after === before) continue;
    fs.writeFileSync(file, after);
    const result = spawnSync('bun', ['install', ...installFlags], {
      cwd: path.join(checkout, project),
      stdio: 'inherit'
    });
    if (result.status !== 0)
      throw new Error(`bun install failed in ${project}`);
    changed.push(project);
  }
  const left = misalignments(checkout);
  if (left.length) throw new Error(`still misaligned:\n${left.join('\n')}`);
  return changed;
}

if (import.meta.main) {
  const args = process.argv.slice(2);
  const named = args[0] !== undefined && !args[0].startsWith('-');
  const checkout = path.resolve(named ? args[0] : repository);
  const changed = sync(checkout, named ? args.slice(1) : args);
  console.log(
    changed.length
      ? `vite-plus ${ownerVersion(checkout)} written to ${changed.join(', ')}`
      : `vite-plus ${ownerVersion(checkout)} already aligned`
  );
}
