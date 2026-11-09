import fs from 'node:fs';
import path from 'node:path';
import { expect, test } from 'vite-plus/test';

import { RELEASE_SOURCE_PATHS } from './artifact-manifest.ts';
import { PAYLOAD_TOOLING_INPUTS } from './payload.ts';

const workspaceRoot = path.resolve(import.meta.dirname, '..');
const installerRoot = path.join(workspaceRoot, 'bazaarplusplus-installer');

// Both lists name release inputs by path and skip a missing path without an
// error, so a rename that misses them would silently drop the file from the
// release source identity or the Payload input digest.
test('every named release source path exists', () => {
  expect(
    RELEASE_SOURCE_PATHS.filter(
      (name) => !fs.existsSync(path.resolve(installerRoot, name))
    )
  ).toEqual([]);
});

test('every named Payload tooling input exists', () => {
  expect(
    PAYLOAD_TOOLING_INPUTS.filter(
      (name) => !fs.existsSync(path.join(workspaceRoot, name))
    )
  ).toEqual([]);
});

// computePayloadInputs hashes release/, the installer's scripts/release/, the
// mod and the named tooling inputs. A release module that starts loading any
// other workspace file at runtime would leave that file out of the digest.
// Type-only imports are erased, so they do not count.
const VALUE_IMPORT =
  /^\s*(?:import|export)\s+(?!type\s)[^;'"]*?\bfrom\s+['"](\.[^'"]+)['"]|^\s*import\s+['"](\.[^'"]+)['"]|\bimport\(\s*['"](\.[^'"]+)['"]\s*\)/gm;
const SCANNED = ['release/', 'bazaarplusplus-installer/scripts/release/'];

test('release modules load no workspace file outside the Payload inputs', () => {
  const pending = [
    ...fs
      .readdirSync(path.join(workspaceRoot, 'release'), { recursive: true })
      .map((name) => `release/${String(name).split(path.sep).join('/')}`)
      .filter(
        (name) =>
          name.endsWith('.ts') &&
          !name.endsWith('.test.ts') &&
          !name.startsWith('release/test-support/')
      )
  ];
  const seen = new Set(pending);
  const outside = [];
  while (pending.length > 0) {
    const file = pending.pop();
    if (file === undefined) break;
    const source = fs.readFileSync(path.join(workspaceRoot, file), 'utf8');
    for (const match of source.matchAll(VALUE_IMPORT)) {
      const specifier = match[1] ?? match[2] ?? match[3];
      if (specifier === undefined) continue;
      const target = path.posix.normalize(
        path.posix.join(path.posix.dirname(file), specifier)
      );
      const covered =
        PAYLOAD_TOOLING_INPUTS.includes(target) ||
        SCANNED.some((prefix) => target.startsWith(prefix));
      if (!covered) outside.push(`${file} -> ${target}`);
      else if (!seen.has(target)) {
        seen.add(target);
        pending.push(target);
      }
    }
  }
  expect(outside).toEqual([]);
});
