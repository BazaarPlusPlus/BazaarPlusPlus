import fs from 'node:fs';
import path from 'node:path';
import { expect, test } from 'vite-plus/test';
import { WORKSPACE_ROOT } from './product.ts';

// The workflows run inline `bun -e` scripts that import release modules by
// path and name. A move or rename that misses one fails only when that
// workflow runs, which for a scheduled watch is long after the change merged.
// scripts/script-references.test.ts checks that the path is tracked; this
// checks that the module still exports each imported name.

const WORKFLOWS = path.join(WORKSPACE_ROOT, '.github', 'workflows');
// The quoted script after `bun -e` or `bun --eval`; it may span lines.
const BUN_EVAL = /\bbun\s+(?:-e|--eval)\s+(["'])([\s\S]*?)\1/g;
// A value import from a release module: named bindings, a default, or both.
const RELEASE_IMPORT =
  /\bimport\s+(?!type\b)(?:(\w+)\s*,?\s*)?(?:\{([^}]*)\})?\s*from\s+['"](\.\/release\/[^'"]+)['"]/g;

/** One name a workflow's `bun -e` script imports from a release module. */
interface WorkflowImport {
  workflow: string;
  module: string;
  name: string;
}

function workflowImports(): WorkflowImport[] {
  const found: WorkflowImport[] = [];
  for (const workflow of fs.readdirSync(WORKFLOWS).sort()) {
    if (!/\.ya?ml$/.test(workflow)) continue;
    const text = fs.readFileSync(path.join(WORKFLOWS, workflow), 'utf8');
    for (const [, , script] of text.matchAll(BUN_EVAL))
      for (const [, defaultName, named, module] of script.matchAll(
        RELEASE_IMPORT
      )) {
        const names = (named ?? '')
          .split(',')
          .map((binding) => binding.trim())
          .filter((binding) => binding && !binding.startsWith('type '))
          .map((binding) => binding.split(/\s+as\s+/)[0]);
        if (defaultName) names.push('default');
        for (const name of names) found.push({ workflow, module, name });
      }
  }
  return found;
}

test('every release import of a workflow bun -e script resolves', async () => {
  const imports = workflowImports();
  // android-apk-watch.yml imports OFFICIAL_ANDROID_APK_URL; a pattern that
  // silently stops matching would pass the check below.
  expect(imports.map(({ name }) => name)).toContain('OFFICIAL_ANDROID_APK_URL');
  const unresolved: string[] = [];
  for (const { workflow, module, name } of imports) {
    const file = path.join(WORKSPACE_ROOT, module);
    if (!fs.existsSync(file)) {
      unresolved.push(`${workflow}: ${module} does not exist`);
      continue;
    }
    const exported = (await import(file)) as Record<string, unknown>;
    if (!Object.hasOwn(exported, name))
      unresolved.push(`${workflow}: ${module} does not export ${name}`);
  }
  expect(unresolved).toEqual([]);
});
