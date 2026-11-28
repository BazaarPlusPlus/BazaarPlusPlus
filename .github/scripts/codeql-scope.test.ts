import assert from 'node:assert/strict';
import fs from 'node:fs';
import { test } from 'vite-plus/test';
import {
  CODEQL_LANGUAGES,
  classifyCodeQLChanges,
  fullCodeQLPlan,
  planCodeQLRun,
  renderCodeQLSummary,
  summarizeCodeQL
} from './codeql-scope.ts';
import type { CodeQLNeeds, CodeQLPlan } from './codeql-scope.ts';

const changed = (...files: string[]) =>
  files.map((file) => ({ status: 'M', file }));

const selected = (plan: CodeQLPlan) =>
  CODEQL_LANGUAGES.filter((lang) => plan[lang].length > 0);

function needsFixture(plan: CodeQLPlan): CodeQLNeeds {
  return {
    classify: {
      result: 'success',
      outputs: {
        plan: JSON.stringify(plan),
        'javascript-typescript': String(
          plan['javascript-typescript'].length > 0
        ),
        python: String(plan.python.length > 0)
      }
    },
    'javascript-typescript': {
      result: plan['javascript-typescript'].length ? 'success' : 'skipped'
    },
    python: {
      result: plan.python.length ? 'success' : 'skipped'
    }
  };
}

test('classifies JS/TS, HTML, manifests and Python separately while skipping unrelated languages', () => {
  assert.deepEqual(
    selected(
      classifyCodeQLChanges(
        changed(
          'README.md',
          'bazaarplusplus-mod/src/BazaarPlusPlus.Core/Example.cs',
          'bazaarplusplus-installer/src-tauri/src/lib.rs',
          'bazaarplusplus-android/app/src/main/java/App.java'
        )
      )
    ),
    []
  );
  for (const file of [
    'bazaarplusplus-site/src/App.tsx',
    'bazaarplusplus-site/index.html',
    'packages/ui/src/Button.ts',
    'package.json',
    'tsconfig.json',
    'bazaarplusplus-installer/scripts/tsconfig.json',
    'bazaarplusplus-server/src/index.mjs'
  ]) {
    assert.deepEqual(
      selected(classifyCodeQLChanges(changed(file))),
      ['javascript-typescript'],
      file
    );
  }
  for (const file of [
    'bazaarplusplus-analyzer/src/bazaarlens/main.py',
    'bazaarplusplus-analyzer/src/bazaarlens/types.pyi',
    'bazaarplusplus-analyzer/pyproject.toml'
  ]) {
    assert.deepEqual(
      selected(classifyCodeQLChanges(changed(file))),
      ['python'],
      file
    );
  }
  for (const file of [
    '.github/workflows/codeql.yml',
    '.github/codeql/codeql-config.yml',
    '.github/scripts/codeql-scope.ts',
    'scripts/git-command.ts'
  ]) {
    assert.deepEqual(
      selected(classifyCodeQLChanges(changed(file))),
      ['javascript-typescript', 'python'],
      file
    );
  }
});

test('schedule, manual runs, failed history fetches and invalid diffs fail open to all languages', () => {
  for (const plan of [
    planCodeQLRun({ eventName: 'schedule', event: {} }),
    planCodeQLRun({ eventName: 'workflow_dispatch', event: {} }),
    planCodeQLRun({
      eventName: 'pull_request',
      event: {
        pull_request: {
          base: { sha: 'a'.repeat(40) },
          head: { sha: 'b'.repeat(40) }
        }
      },
      historyFetchOutcome: 'failure'
    }),
    planCodeQLRun({ eventName: 'push', event: {} }),
    classifyCodeQLChanges(null),
    classifyCodeQLChanges([])
  ]) {
    assert.deepEqual(selected(plan), ['javascript-typescript', 'python']);
  }
});

test('summary requires every selected language to succeed and allows skipped unaffected languages', () => {
  assert.equal(summarizeCodeQL(needsFixture(fullCodeQLPlan('all'))).ok, true);
  assert.equal(
    summarizeCodeQL(needsFixture(classifyCodeQLChanges(changed('README.md'))))
      .ok,
    true
  );

  const jsOnly = needsFixture(
    classifyCodeQLChanges(changed('packages/ui/src/Button.tsx'))
  );
  assert.equal(summarizeCodeQL(jsOnly).ok, true);
  assert.match(renderCodeQLSummary(summarizeCodeQL(jsOnly)), /不受影响/);

  for (const badResult of ['failure', 'cancelled', 'skipped', undefined]) {
    const broken = needsFixture(fullCodeQLPlan('all'));
    broken.python = { result: badResult };
    assert.equal(summarizeCodeQL(broken).ok, false, `python: ${badResult}`);
  }

  const badClassify = needsFixture(fullCodeQLPlan('all'));
  badClassify.classify!.result = 'failure';
  assert.equal(summarizeCodeQL(badClassify).ok, false);

  const inconsistent = needsFixture(fullCodeQLPlan('all'));
  inconsistent.classify!.outputs!.python = 'false';
  assert.equal(summarizeCodeQL(inconsistent).ok, false);
});

test('codeql.yml wires classify, explicit language jobs, summary and non-cancelling master push', () => {
  const workflow = fs.readFileSync(
    new URL('../workflows/codeql.yml', import.meta.url),
    'utf8'
  );
  assert.match(
    workflow,
    /cancel-in-progress:\s*\$\{\{\s*!\(github\.event_name == 'push' && github\.ref == 'refs\/heads\/master'\)\s*\}\}/
  );
  assert.ok(workflow.includes('name: CodeQL (javascript-typescript)'));
  assert.ok(workflow.includes('name: CodeQL (python)'));
  assert.ok(workflow.includes('name: CodeQL summary'));
  assert.ok(!workflow.includes('matrix.'));
  assert.match(workflow, /git fetch --no-tags --unshallow origin/);
});
