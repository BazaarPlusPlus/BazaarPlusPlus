import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { expect, test } from 'vitest';

import {
  bindingsAreFresh,
  commitGeneratedBindings,
  runGenerateBindings
} from './generate-bindings.mjs';

test('standalone binding generation runs Rust tests without requiring release resources', () => {
  const rootDir = fs.mkdtempSync(path.join(os.tmpdir(), 'bpp-bindings-test-'));
  try {
    runGenerateBindings(rootDir, {
      runAllRustTests: true,
      run(_command, _args, { env }) {
        expect(JSON.parse(env.TAURI_CONFIG || '{}').bundle?.resources).toEqual(
          []
        );
        fs.writeFileSync(
          env.BPP_SPECTA_EXPORT_PATH,
          'export const commands = {};\n'
        );
      }
    });
    expect(
      fs.readFileSync(
        path.join(rootDir, 'src/types/generated/commands.ts'),
        'utf8'
      )
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

test('binding freshness only skips when every input predates the artifact', () => {
  expect(bindingsAreFresh(200, [100, 150, 199])).toBe(true);
  expect(bindingsAreFresh(200, [100, 250])).toBe(false);
  // An input written in the same millisecond is treated as a change.
  expect(bindingsAreFresh(200, [200])).toBe(false);
  // Missing artifact or unreadable inputs must never skip regeneration.
  expect(bindingsAreFresh(null, [100])).toBe(false);
  expect(bindingsAreFresh(200, [])).toBe(false);
});
