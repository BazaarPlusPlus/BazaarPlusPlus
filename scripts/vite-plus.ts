// Keeps the projects on Vite+ (ADR 0017) on one version. Root package.json
// `vite-plus` owns it; `just vite-plus-sync` copies it into the other
// projects and into every `vite` alias, which `vp` requires to name the same
// core version. Dependabot does not update `npm:` aliases.
import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';

const root = path.resolve(import.meta.dirname, '..');

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

function manifestPath(project: string): string {
  return path.join(root, project, 'package.json');
}

function readManifest(project: string): Manifest {
  return JSON.parse(fs.readFileSync(manifestPath(project), 'utf8'));
}

/** The `vite-plus` version in root package.json. */
export function ownerVersion(): string {
  const version = readManifest('.').devDependencies?.['vite-plus'];
  if (!version) throw new Error('root package.json has no vite-plus');
  return version;
}

/** Each pin that differs from the owner version, as `<file> <field>: <value>`. */
export function misalignments(version = ownerVersion()): string[] {
  const found: string[] = [];
  for (const project of VITE_PLUS_PROJECTS) {
    const manifest = readManifest(project);
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

if (import.meta.main) {
  const version = ownerVersion();
  for (const project of VITE_PLUS_PROJECTS) {
    const file = manifestPath(project);
    fs.writeFileSync(file, pinText(fs.readFileSync(file, 'utf8'), version));
    const result = spawnSync('bun', ['install'], {
      cwd: path.join(root, project),
      stdio: 'inherit'
    });
    if (result.status !== 0) process.exit(result.status ?? 1);
  }
  const left = misalignments(version);
  if (left.length) {
    console.error(left.join('\n'));
    process.exit(1);
  }
  console.log(`vite-plus ${version} in ${VITE_PLUS_PROJECTS.join(', ')}`);
}
