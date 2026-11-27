import fs from 'node:fs';
import path from 'node:path';

export interface PackageManifest {
  name?: string;
  version?: string;
  exports?: Record<string, unknown>;
  dependencies?: Record<string, string>;
  peerDependencies?: Record<string, string>;
  [key: string]: unknown;
}

function stripExportValue(value: unknown): unknown {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return value;
  const out: Record<string, unknown> = {};
  for (const [key, entry] of Object.entries(value)) {
    if (key === '@bpp/source') continue;
    out[key] = stripExportValue(entry);
  }
  return out;
}

export function preparePublishManifest(
  manifest: PackageManifest
): PackageManifest {
  const next: PackageManifest = { ...manifest };
  if (manifest.exports) {
    next.exports = stripExportValue(manifest.exports) as Record<
      string,
      unknown
    >;
  }
  for (const field of ['dependencies', 'peerDependencies'] as const) {
    const deps = next[field];
    if (!deps) continue;
    for (const [name, spec] of Object.entries(deps)) {
      if (spec.startsWith('workspace:')) {
        throw new Error(
          `Publishable manifest cannot retain workspace protocol: ${field}.${name} = ${spec}`
        );
      }
    }
  }
  return next;
}

if (import.meta.main) {
  const args = process.argv.slice(2);
  const apply = args[0] === '--apply';
  const target = path.resolve(
    apply ? (args[1] ?? 'package.json') : (args[0] ?? 'package.json')
  );
  const raw = JSON.parse(fs.readFileSync(target, 'utf8')) as PackageManifest;
  const cleaned = preparePublishManifest(raw);
  const serialized = `${JSON.stringify(cleaned, null, 2)}\n`;
  if (apply) {
    fs.writeFileSync(target, serialized);
  } else {
    process.stdout.write(serialized);
  }
}
