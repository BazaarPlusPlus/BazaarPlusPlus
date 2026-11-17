import assert from 'node:assert/strict';
import { test } from 'vite-plus/test';
import { misalignments } from './vite-plus.ts';

test('every Vite+ project pins the root vite-plus version and aliases vite to its core', () => {
  assert.deepEqual(
    misalignments(),
    [],
    'Run `just vite-plus-sync` to copy root package.json vite-plus into every project.'
  );
});
