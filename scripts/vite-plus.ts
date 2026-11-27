// Dependabot leaves npm: aliases untouched, so vite-plus-sync.yml runs this on its PRs.
import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';

const repository = path.resolve(import.meta.dirname, '..');

export const VITE_PLUS_PROJECTS = [
  '.',
  'packages/ui',
  'bazaarplusplus-site',
  'bazaarplusplus-ui-docs',
  'bazaarplusplus-installer'
];

const CORE = 'npm:@voidzero-dev/vite-plus-core@';

interface Manifest {
  devDependencies?: Record<string, string>;
  overrides?: Record<string, unknown>;
}

export interface LockedVitePlus {
  version: string;
  coreSpec: string;
}

function manifestPath(checkout: string, project: string): string {
  return path.join(checkout, project, 'package.json');
}

function readManifest(checkout: string, project: string): Manifest {
  return JSON.parse(fs.readFileSync(manifestPath(checkout, project), 'utf8'));
}

export function ownerVersion(checkout = repository): string {
  const version = readManifest(checkout, '.').devDependencies?.['vite-plus'];
  if (!version) throw new Error('root package.json has no vite-plus');
  return version;
}

// Since vite-plus 1.7.0, @voidzero-dev/vite-plus-core versions independently of
// vite-plus (encoding the bundled Vite version), so read its specifier from the
// resolved vite-plus entry in root bun.lock rather than reusing ownerVersion.
export function readLockedVitePlus(
  checkout = repository
): LockedVitePlus | null {
  const lockPath = path.join(checkout, 'bun.lock');
  if (!fs.existsSync(lockPath)) return null;
  const lock = fs.readFileSync(lockPath, 'utf8');
  const match = /^ {4}"vite-plus": (\[.*\]),?$/m.exec(lock);
  if (!match?.[1]) return null;
  const entry = JSON.parse(match[1]) as [
    unknown,
    unknown,
    { dependencies?: Record<string, unknown> }?
  ];
  const resolved = entry[0];
  const coreSpec = entry[2]?.dependencies?.vite;
  if (
    typeof resolved !== 'string' ||
    !resolved.startsWith('vite-plus@') ||
    typeof coreSpec !== 'string' ||
    !coreSpec.startsWith(CORE)
  )
    return null;
  return {
    version: resolved.slice('vite-plus@'.length),
    coreSpec
  };
}

export function ownerCoreSpec(checkout = repository): string {
  const version = ownerVersion(checkout);
  const locked = readLockedVitePlus(checkout);
  if (!locked)
    throw new Error('root bun.lock has no vite-plus core dependency');
  if (locked.version !== version)
    throw new Error(
      `root bun.lock resolves vite-plus@${locked.version}, expected ${version}`
    );
  return locked.coreSpec;
}

export function misalignments(checkout = repository): string[] {
  const version = ownerVersion(checkout);
  const locked = readLockedVitePlus(checkout);
  if (!locked) return ['bun.lock vite-plus: missing'];
  if (locked.version !== version)
    return [`bun.lock vite-plus: ${locked.version} (expected ${version})`];
  const core = locked.coreSpec;
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
      ['devDependencies.vite', manifest.devDependencies?.vite, core],
      ...(project === '.'
        ? ([['overrides.vite', manifest.overrides?.vite, core]] as [
            string,
            unknown,
            string
          ][])
        : [])
    ];
    for (const [field, actual, expected] of pins)
      if (actual !== expected)
        found.push(`${file} ${field}: ${String(actual)}`);
  }
  return found;
}

// Rewrites the version text in place so the file keeps its formatting.
function pinText(text: string, version: string, coreSpec: string): string {
  return text
    .replace(/("vite-plus":\s*")[^"]*"/, () => `"vite-plus": "${version}"`)
    .replaceAll(/npm:@voidzero-dev\/vite-plus-core@[^"]*/g, () => coreSpec);
}

function runBunInstall(
  checkout: string,
  project: string,
  installFlags: string[]
) {
  const result = spawnSync('bun', ['install', ...installFlags], {
    cwd: path.join(checkout, project),
    stdio: 'inherit'
  });
  if (result.status !== 0) throw new Error(`bun install failed in ${project}`);
}

export function sync(checkout = repository, installFlags: string[] = []) {
  const version = ownerVersion(checkout);
  const changed = new Set<string>();
  // A manual root package.json edit leaves bun.lock on the old vite-plus;
  // resolve root first so we can read the new release's core dependency.
  if (readLockedVitePlus(checkout)?.version !== version) {
    runBunInstall(checkout, '.', installFlags);
    changed.add('.');
  }
  const coreSpec = ownerCoreSpec(checkout);
  let needsLockSync = false;
  for (const project of VITE_PLUS_PROJECTS) {
    const file = manifestPath(checkout, project);
    const before = fs.readFileSync(file, 'utf8');
    const after = pinText(before, version, coreSpec);
    if (after === before) continue;
    fs.writeFileSync(file, after);
    changed.add(project);
    needsLockSync = true;
  }
  if (needsLockSync) runBunInstall(checkout, '.', installFlags);
  const left = misalignments(checkout);
  if (left.length) throw new Error(`still misaligned:\n${left.join('\n')}`);
  return [...changed];
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
