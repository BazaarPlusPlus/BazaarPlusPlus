import assert from 'node:assert/strict';
import fs from 'node:fs';
import test from 'node:test';
import {
  SCOPES,
  classifyChanges,
  fullScope,
  manualScope
} from './checks-scope.mjs';
import { summarize, renderSummary } from './checks-summary.mjs';

function fixture(plan = fullScope('fixture')) {
  const outputs = {
    plan: JSON.stringify(plan),
    compatibility: 'false',
    performance: 'false',
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
        SCOPES.map((job) => [
          job,
          { result: plan[job].length ? 'success' : 'skipped' }
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

test('all selected jobs must succeed, including each selected native platform', () => {
  assert.equal(summarize(fixture()).ok, true);
  for (const job of SCOPES)
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
      f.needs.classify.outputs['installer-frontend'] = 'false';
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
  f.needs['mod-android'].result = 'skipped';
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

test('workflow wiring summarizes every optional job and isolates every manual run', () => {
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
    /name: \$\{\{ github.event_name == 'workflow_dispatch' && format\('Manual checks \(\{0\}\/\{1\}\)', inputs.scope, inputs.platform\) \|\| 'Checks summary' \}\}/
  );
  const needs = summary
    .match(/needs:\s*\[([^\]]+)\]/)[1]
    .split(',')
    .map((job) => job.trim());
  assert.deepEqual(needs.sort(), ['classify', ...SCOPES].sort());
  assert.match(workflow, /group: checks-\$\{\{ github.event_name \}\}/);
  assert.match(workflow, /group: .*inputs.scope.*inputs.platform/);
  assert.match(workflow, /shared-key: installer-.*-installer\n/);

  assert.match(
    workflow,
    /save-if: \$\{\{ github.event_name == 'push' && github.ref == 'refs\/heads\/master' \}\}/
  );
  for (const name of [
    'site (full check and test)',
    'server (full check and test)',
    'Installer (frontend)',
    'Installer (macos-26)',
    'Installer (windows-latest)',
    'Mod (macos-26)',
    'Mod (windows-latest)'
  ])
    assert.ok(workflow.includes(`    name: ${name}\n`));
  for (const job of SCOPES) {
    const block = workflow.split(`\n  ${job}:\n`)[1]?.split(/\n  [\w-]+:\n/)[0];
    assert.ok(
      block?.includes(`needs.classify.outputs.${job} == 'true'`),
      `${job} consumes its selection`
    );
  }
  assert.ok(
    !workflow.includes('matrix.'),
    'skipped matrix parents do not emit legacy child names'
  );
});

test('manual installer/windows requires frontend and Windows but not macOS or Mod', () => {
  const inputs = { scope: 'installer', platform: 'windows' };
  const f = fixture(manualScope(inputs));
  f.eventName = 'workflow_dispatch';
  f.event.inputs = inputs;
  assert.equal(summarize(f).ok, true);
  assert.equal(f.needs['installer-macos'].result, 'skipped');
  for (const job of ['installer-frontend', 'installer-windows']) {
    const broken = structuredClone(f);
    broken.needs[job].result = 'skipped';
    assert.equal(summarize(broken).ok, false);
  }
  const report = renderSummary(summarize(f), {
    eventName: f.eventName,
    ref: 'refs/heads/fixture',
    sha: 'abc',
    inputs
  });
  assert.match(report, /Coverage: installer\/windows/);
  assert.match(report, /Ref: refs\/heads\/fixture · SHA: abc/);
});

test('frontend-only cannot hide a skipped selected frontend or require native credentials', () => {
  const f = fixture(
    classifyChanges([
      { status: 'M', file: 'bazaarplusplus-installer/src/App.tsx' }
    ])
  );
  f.modCredentials = 'false';
  assert.equal(summarize(f).ok, true);
  assert.equal(f.needs['installer-windows'].result, 'skipped');
  f.needs['installer-frontend'].result = 'skipped';
  assert.equal(summarize(f).ok, false);
});

test('daily Mod checks accept deferred Windows but require both selected hosts', () => {
  const f = fixture(
    classifyChanges([
      {
        status: 'M',
        file: 'bazaarplusplus-mod/src/BazaarPlusPlus.Core/Example.cs'
      }
    ])
  );
  assert.equal(f.needs['mod-windows'].result, 'skipped');
  assert.equal(summarize(f).ok, true);
  for (const job of ['mod-macos', 'mod-android']) {
    const broken = structuredClone(f);
    broken.needs[job].result = 'skipped';
    assert.equal(summarize(broken).ok, false);
  }
});

test('summary distinguishes daily, snapshot and manual desktop verification', () => {
  for (const [compatibility, performance, expected] of [
    ['false', 'false', ['online']],
    ['true', 'false', ['online', 'staging / ptr']],
    ['true', 'true', ['online', 'staging / ptr', 'performance']]
  ]) {
    const f = fixture();
    Object.assign(f.needs.classify.outputs, { compatibility, performance });
    const result = summarize(f);
    assert.equal(result.ok, true);
    assert.deepEqual(result.coverage, expected);
    assert.ok(
      renderSummary(result, {
        eventName: f.eventName,
        ref: 'refs/heads/test',
        sha: 'abc'
      }).includes(`Planned Mod desktop checks: ${expected.join(' + ')}`)
    );
  }
  const android = fixture(manualScope({ scope: 'mod', platform: 'android' }));
  assert.deepEqual(summarize(android).coverage, []);
  for (const flag of ['compatibility', 'performance']) {
    const f = fixture();
    delete f.needs.classify.outputs[flag];
    assert.equal(summarize(f).ok, false, `missing ${flag}`);
    f.needs.classify.outputs[flag] = 'unknown';
    assert.equal(summarize(f).ok, false, `invalid ${flag}`);
  }
});

test('both desktop jobs consume classified extended checks, with release preflight before packaging', () => {
  const workflow = fs.readFileSync(
    new URL('../workflows/checks.yml', import.meta.url),
    'utf8'
  );
  const desktop = workflow
    .split('\n  mod-macos:\n')[1]
    .split('\n  mod-windows:\n')[0];
  for (const [flag, command] of [
    ['compatibility', 'just mod::matrix staging ptr'],
    ['performance', 'just verify mod::test-performance']
  ]) {
    assert.ok(
      workflow.includes(`${flag}: \${{ steps.scope.outputs.${flag} }}`)
    );
    assert.ok(
      desktop.includes(
        `- if: needs.classify.outputs.${flag} == 'true'\n        run: ${command}`
      )
    );
  }
  const windows = workflow
    .split('\n  mod-windows:\n')[1]
    .split('\n  mod-android:\n')[0];
  assert.ok(windows.includes('steps: *mod-steps'));
  const release = fs.readFileSync(
    new URL('../workflows/release.yml', import.meta.url),
    'utf8'
  );
  const preflight = release.slice(
    release.indexOf('- name: Verify the desktop Mod before release'),
    release.indexOf('- name: Prepare the Payload')
  );
  assert.ok(
    preflight.includes(
      "if: env.STAGE == 'release' && matrix.platform != 'android'"
    )
  );
  assert.ok(
    preflight.includes('just verify mod::check mod::test mod::test-performance')
  );
  assert.ok(preflight.includes('just mod::matrix staging ptr'));
  assert.ok(
    preflight.includes(
      "if: env.STAGE == 'release' && matrix.platform == 'android'"
    )
  );
  assert.ok(
    preflight.includes('just verify mod::android-build mod::test-core')
  );
});
