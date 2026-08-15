import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { expect, test } from 'vitest';

import {
  commitGeneratedBindings,
  replaceDirectoryWithBackup
} from './generate-bindings.mjs';

test('generated bindings commit atomically replaces the complete target', () => {
  const rootDir = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-bindings-test-'));
  const generatedDir = path.join(rootDir, 'src/types/generated');
  const tempGeneratedDir = path.join(rootDir, 'staged-generated');

  try {
    fs.mkdirSync(generatedDir, { recursive: true });
    fs.mkdirSync(tempGeneratedDir, { recursive: true });
    fs.writeFileSync(path.join(generatedDir, 'old.ts'), 'old only');
    fs.writeFileSync(
      path.join(tempGeneratedDir, 'commands.ts'),
      'export const commands = {};\r\n'
    );

    commitGeneratedBindings({ generatedDir, tempGeneratedDir });

    expect(fs.existsSync(path.join(generatedDir, 'old.ts'))).toBe(false);
    expect(
      fs.readFileSync(path.join(generatedDir, 'commands.ts'), 'utf8')
    ).toBe('export const commands = {};\n');
  } finally {
    fs.rmSync(rootDir, { recursive: true, force: true });
  }
});

test('generated bindings validation rejects a missing commands artifact', () => {
  const rootDir = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-bindings-test-'));
  const generatedDir = path.join(rootDir, 'src/types/generated');
  const tempGeneratedDir = path.join(rootDir, 'staged-generated');

  try {
    fs.mkdirSync(generatedDir, { recursive: true });
    fs.mkdirSync(tempGeneratedDir, { recursive: true });
    fs.writeFileSync(path.join(generatedDir, 'preserved.ts'), 'preserved');

    expect(() =>
      commitGeneratedBindings({ generatedDir, tempGeneratedDir })
    ).toThrow(/commands\.ts/);
    expect(
      fs.readFileSync(path.join(generatedDir, 'preserved.ts'), 'utf8')
    ).toBe('preserved');
  } finally {
    fs.rmSync(rootDir, { recursive: true, force: true });
  }
});

test('generated bindings validation rejects an empty commands artifact', () => {
  const rootDir = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-bindings-test-'));
  const generatedDir = path.join(rootDir, 'src/types/generated');
  const tempGeneratedDir = path.join(rootDir, 'staged-generated');

  try {
    fs.mkdirSync(tempGeneratedDir, { recursive: true });
    fs.writeFileSync(path.join(tempGeneratedDir, 'commands.ts'), '\n');

    expect(() =>
      commitGeneratedBindings({ generatedDir, tempGeneratedDir })
    ).toThrow(/empty or malformed/);
  } finally {
    fs.rmSync(rootDir, { recursive: true, force: true });
  }
});

test('failed backup rename preserves the last valid generated directory', () => {
  const rootDir = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-bindings-test-'));
  const generatedDir = path.join(rootDir, 'src/types/generated');
  const sourceDir = path.join(rootDir, 'staged-generated');

  try {
    fs.mkdirSync(generatedDir, { recursive: true });
    fs.mkdirSync(sourceDir, { recursive: true });
    fs.writeFileSync(path.join(generatedDir, 'commands.ts'), 'old bindings');
    fs.writeFileSync(path.join(sourceDir, 'commands.ts'), 'new bindings');

    expect(() =>
      replaceDirectoryWithBackup(generatedDir, sourceDir, {
        renameSync(source, destination) {
          if (source === generatedDir) throw new Error('backup rename failed');
          fs.renameSync(source, destination);
        }
      })
    ).toThrow('backup rename failed');

    expect(
      fs.readFileSync(path.join(generatedDir, 'commands.ts'), 'utf8')
    ).toBe('old bindings');
  } finally {
    fs.rmSync(rootDir, { recursive: true, force: true });
  }
});
