import fs from 'node:fs';
import path from 'node:path';
import { expect, test } from 'vite-plus/test';
import { WORKSPACE_ROOT } from './product.ts';

// Workflows must call documented CLI verbs instead of inline bun -e / bun -p scripts that import release modules directly.

const WORKFLOWS = path.join(WORKSPACE_ROOT, '.github', 'workflows');
const BUN_EVAL = /\bbun\s+(?:-e|--eval|-p|--print)\s+(["'])([\s\S]*?)\1/g;
const RELEASE_REF = /\.\/release\/|game-libs\.lock\.json/;

function inlineReleaseRefs(): string[] {
  const found: string[] = [];
  for (const workflow of fs.readdirSync(WORKFLOWS).sort()) {
    if (!/\.ya?ml$/.test(workflow)) continue;
    const text = fs.readFileSync(path.join(WORKFLOWS, workflow), 'utf8');
    for (const [, , script] of text.matchAll(BUN_EVAL)) {
      if (RELEASE_REF.test(script)) found.push(`${workflow}: ${script}`);
    }
  }
  return found;
}

test('no workflow uses inline bun -e or bun -p to import release modules or read the lock directly', () => {
  expect(inlineReleaseRefs()).toEqual([]);
});
