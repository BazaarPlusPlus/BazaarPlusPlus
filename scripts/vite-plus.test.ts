import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { test } from 'vite-plus/test';
import {
  VITE_PLUS_PROJECTS,
  misalignments,
  readLockedVitePlus
} from './vite-plus.ts';

function writeFixture(
  root: string,
  {
    lockVersion,
    lockCore,
    projects
  }: {
    lockVersion: string;
    lockCore: string;
    projects: Record<
      string,
      { vitePlus: string; vite: string; override: string }
    >;
  }
) {
  fs.writeFileSync(
    path.join(root, 'bun.lock'),
    `{\n  "packages": {\n    "vite-plus": ["vite-plus@${lockVersion}", "", { "dependencies": { "vite": "${lockCore}" } }, "sha512-test"],\n  }\n}\n`
  );
  for (const project of VITE_PLUS_PROJECTS) {
    const spec = projects[project]!;
    const dir = path.join(root, project);
    fs.mkdirSync(dir, { recursive: true });
    fs.writeFileSync(
      path.join(dir, 'package.json'),
      JSON.stringify(
        {
          devDependencies: {
            vite: spec.vite,
            'vite-plus': spec.vitePlus
          },
          overrides: {
            vite: spec.override
          }
        },
        null,
        2
      )
    );
  }
}

test('every Vite+ project pins the root vite-plus version and aliases vite to its core', () => {
  assert.deepEqual(
    misalignments(),
    [],
    'Run `just vite-plus-sync` to copy root package.json vite-plus into every project.'
  );
});

test('misalignments validates core aliases against the version locked by vite-plus in bun.lock', () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-vite-plus-'));
  try {
    const core = 'npm:@voidzero-dev/vite-plus-core@8.3.8000';
    const aligned = Object.fromEntries(
      VITE_PLUS_PROJECTS.map((project) => [
        project,
        { vitePlus: '1.7.0', vite: core, override: core }
      ])
    );
    writeFixture(dir, {
      lockVersion: '1.7.0',
      lockCore: core,
      projects: aligned
    });

    assert.deepEqual(readLockedVitePlus(dir), {
      version: '1.7.0',
      coreSpec: core
    });
    assert.deepEqual(misalignments(dir), []);

    writeFixture(dir, {
      lockVersion: '1.7.0',
      lockCore: core,
      projects: {
        ...aligned,
        'bazaarplusplus-site': {
          vitePlus: '1.7.0',
          vite: 'npm:@voidzero-dev/vite-plus-core@1.6.0',
          override: core
        }
      }
    });
    assert.deepEqual(misalignments(dir), [
      'bazaarplusplus-site/package.json devDependencies.vite: npm:@voidzero-dev/vite-plus-core@1.6.0'
    ]);

    writeFixture(dir, {
      lockVersion: '1.6.0',
      lockCore: 'npm:@voidzero-dev/vite-plus-core@1.6.0',
      projects: aligned
    });
    assert.deepEqual(misalignments(dir), [
      'bun.lock vite-plus: 1.6.0 (expected 1.7.0)'
    ]);
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});
