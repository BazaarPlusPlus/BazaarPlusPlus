import { mkdtempSync, mkdirSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { test, expect } from 'vitest';

import {
  assertMacosTrampolineStub,
  assertMacosTrampolineStubWith,
  macosTrampolineStubPath
} from './prebuild-check.mjs';

test('trampoline stub check fails loudly when the compiled stub is missing', () => {
  const root = mkdtempSync(path.join(tmpdir(), 'bpp-stub-'));
  expect(() => assertMacosTrampolineStub(root)).toThrow(
    'Missing compiled macOS trampoline stub'
  );
});

test('trampoline stub check rejects a non-Mach-O stub', () => {
  const root = mkdtempSync(path.join(tmpdir(), 'bpp-stub-'));
  const stubPath = macosTrampolineStubPath(root);
  mkdirSync(path.dirname(stubPath), { recursive: true });
  writeFileSync(stubPath, 'not a mach-o binary');
  expect(() => assertMacosTrampolineStubWith(root, () => 'ASCII text')).toThrow(
    'not arm64 Mach-O'
  );
});
