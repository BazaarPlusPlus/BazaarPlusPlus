import assert from 'node:assert/strict';
import fs from 'node:fs';
import test from 'node:test';
import { classifyChanges, fullScope } from './checks-scope.mjs';
import { JOB_SCOPES, summarize } from './checks-summary.mjs';

function fixture(plan = fullScope('fixture')) {
  const outputs = {
    plan: JSON.stringify(plan),
    ...Object.fromEntries(
      Object.entries(plan).map(([scope, reasons]) => [
        scope,
        String(reasons.length > 0)
      ])
    )
  };
  return {
    needs: {
      classify: { result: 'success', outputs },
      ...Object.fromEntries(
        Object.entries(JOB_SCOPES).map(([job, scope]) => [
          job,
          { result: plan[scope].length ? 'success' : 'skipped' }
        ])
      )
    },
    eventName: 'pull_request',
    event: {
      repository: { full_name: 'owner/repo' },
      pull_request: { head: { repo: { full_name: 'owner/repo' } } }
    },
    modCredentials: 'true'
  };
}

test('all selected jobs must succeed, including each native platform and timing report', () => {
  assert.equal(summarize(fixture()).ok, true);
  for (const job of Object.keys(JOB_SCOPES))
    for (const result of ['failure', 'cancelled', 'skipped', undefined]) {
      const f = fixture();
      f.needs[job].result = result;
      assert.equal(summarize(f).ok, false, `${job}: ${result}`);
    }
});

test('unaffected jobs show why they are skipped', () => {
  const result = summarize(
    fixture(
      classifyChanges([
        { status: 'M', file: 'bazaarplusplus-server/package-lock.json' }
      ])
    )
  );
  assert.equal(result.ok, true);
  assert.equal(
    result.rows.find(({ job }) => job === 'installer-macos').status,
    '不受影响'
  );
  assert.match(
    result.rows.find(({ job }) => job === 'server').reasons[0],
    /package-lock/
  );
});

test('classification failure, missing output, malformed and inconsistent selections fail closed', () => {
  for (const modify of [
    (f) => {
      f.needs.classify.result = 'failure';
    },
    (f) => {
      f.needs.classify.result = 'cancelled';
    },
    (f) => {
      f.needs.classify.result = 'skipped';
    },
    (f) => {
      f.needs.classify.outputs.plan = '{}';
    },
    (f) => {
      f.needs.classify.outputs.plan = 'invalid JSON';
    },
    (f) => {
      f.needs.classify.outputs.installer = 'false';
    },
    (f) => {
      delete f.needs.classify;
    }
  ]) {
    const f = fixture();
    modify(f);
    assert.equal(summarize(f).ok, false);
  }
});

test('internal and Dependabot Mod selections fail without credentials; forks have an explicit exception', () => {
  const f = fixture();
  f.modCredentials = 'false';
  assert.equal(summarize(f).ok, false);
  f.event.pull_request.user = { login: 'dependabot[bot]' };
  assert.equal(summarize(f).ok, false);
  f.event.pull_request.head.repo.full_name = 'fork/repo';
  f.needs['mod-macos'].result = 'skipped';
  f.needs['mod-windows'].result = 'skipped';
  const result = summarize(f);
  assert.equal(result.ok, true);
  assert.match(
    result.rows.find(({ job }) => job === 'mod-macos').status,
    /fork 例外/
  );
  f.needs['mod-macos'].result = 'failure';
  assert.equal(
    summarize(f).ok,
    false,
    'fork exception cannot hide a failed job'
  );
  f.eventName = 'workflow_dispatch';
  assert.equal(summarize(f).ok, false, 'fork exception is PR-only');
});

test('workflow wiring preserves fixed old names, always summarizes every optional job, and isolates manual runs', () => {
  const workflow = fs.readFileSync(
    new URL('../workflows/checks.yml', import.meta.url),
    'utf8'
  );
  const summary = workflow.slice(
    workflow.indexOf('\n  summary:\n'),
    workflow.indexOf('\n  bot-mod-boundary:\n')
  );
  assert.match(summary, /if: always\(\)/);
  assert.match(
    summary,
    /name: \$\{\{ github.event_name == 'workflow_dispatch' && 'Manual checks summary' \|\| 'Checks summary' \}\}/
  );
  const needs = summary
    .match(/needs:\s*\[([^\]]+)\]/)[1]
    .split(',')
    .map((job) => job.trim());
  assert.deepEqual(
    needs.sort(),
    ['classify', ...Object.keys(JOB_SCOPES)].sort()
  );
  assert.match(workflow, /group: checks-\$\{\{ github.event_name \}\}/);
  assert.match(
    workflow,
    /save-if: \$\{\{ github.event_name == 'push' && github.ref == 'refs\/heads\/master' \}\}/
  );
  for (const name of [
    'site (full check and test)',
    'server (full check and test)',
    'Installer (macos-14)',
    'Installer (windows-latest)',
    'Mod (macos-14)',
    'Mod (windows-latest)'
  ])
    assert.ok(workflow.includes(`    name: ${name}\n`));
  for (const [job, scope] of Object.entries(JOB_SCOPES)) {
    const block = workflow.split(`\n  ${job}:\n`)[1]?.split(/\n  [\w-]+:\n/)[0];
    assert.ok(
      block?.includes(`needs.classify.outputs.${scope} != 'false'`),
      `${job} consumes ${scope}`
    );
    assert.ok(
      block?.includes(`needs.classify.result != 'success'`),
      `${job} runs if classification fails during migration`
    );
  }
  assert.ok(
    !workflow.includes('matrix.'),
    'skipped matrix parents do not emit legacy child names'
  );
});
