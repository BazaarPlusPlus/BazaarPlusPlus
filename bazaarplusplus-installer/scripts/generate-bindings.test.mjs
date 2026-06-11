import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { expect, test } from 'vitest';

import {
  buildGeneratedBarrelSource,
  buildTauriCommandNamesSource,
  commitGeneratedBindings
} from './generate-bindings.mjs';

test('generated bindings commit replaces the target from staged output', () => {
  const rootDir = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-bindings-test-'));
  const generatedDir = path.join(rootDir, 'src/types/generated');
  const bindingsDir = path.join(generatedDir, 'bindings');
  const tempBindingsDir = path.join(rootDir, 'staged-bindings');
  const barrelPath = path.join(generatedDir, 'index.ts');
  const commandNamesPath = path.join(generatedDir, 'tauri-command-names.ts');

  try {
    fs.mkdirSync(bindingsDir, { recursive: true });
    fs.mkdirSync(tempBindingsDir, { recursive: true });
    fs.writeFileSync(path.join(bindingsDir, 'OldOnly.ts'), 'old only');
    fs.writeFileSync(path.join(bindingsDir, 'Shared.ts'), 'old shared');
    fs.writeFileSync(barrelPath, 'old barrel');
    fs.writeFileSync(commandNamesPath, 'old commands');

    fs.writeFileSync(path.join(tempBindingsDir, 'NewOnly.ts'), 'new only');
    fs.writeFileSync(path.join(tempBindingsDir, 'Shared.ts'), 'new shared');

    commitGeneratedBindings({
      bindingsDir,
      tempBindingsDir,
      barrelPath,
      commandNamesPath,
      typeFiles: ['NewOnly.ts', 'Shared.ts'],
      commandNames: ['get_app_bootstrap', 'install_mod']
    });

    expect(fs.existsSync(path.join(bindingsDir, 'OldOnly.ts'))).toBe(false);
    expect(fs.readFileSync(path.join(bindingsDir, 'Shared.ts'), 'utf8')).toBe(
      'new shared'
    );
    expect(fs.readFileSync(path.join(bindingsDir, 'NewOnly.ts'), 'utf8')).toBe(
      'new only'
    );
    expect(fs.readFileSync(barrelPath, 'utf8')).toBe(
      buildGeneratedBarrelSource(['NewOnly.ts', 'Shared.ts'])
    );
    expect(fs.readFileSync(commandNamesPath, 'utf8')).toBe(
      buildTauriCommandNamesSource(['get_app_bootstrap', 'install_mod'])
    );
  } finally {
    fs.rmSync(rootDir, { recursive: true, force: true });
  }
});
