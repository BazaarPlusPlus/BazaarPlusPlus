import fs from 'node:fs';
import path from 'node:path';
import { expect, test } from 'vite-plus/test';
import { WORKSPACE_ROOT } from './product.ts';

// Scheduled workflows may run long after a release export was renamed. Check their inline imports here.

const WORKFLOWS = path.join(WORKSPACE_ROOT, '.github', 'workflows');
const BUN_EVAL = /\bbun\s+(?:-e|--eval)\s+(["'])([\s\S]*?)\1/g;
const RELEASE_IMPORT =
  /\bimport\s+(?!type\b)(?:(\w+)\s*,?\s*)?(?:\{([^}]*)\})?\s*from\s+['"](\.\/release\/[^'"]+)['"]/g;

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
