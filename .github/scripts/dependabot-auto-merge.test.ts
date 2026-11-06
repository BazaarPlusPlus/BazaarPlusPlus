import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { test } from 'vitest';
import { run } from './dependabot-auto-merge.ts';
import type {
  GithubScriptArgs,
  PullRequest,
  PullRequestNode
} from './dependabot-auto-merge.ts';

interface FixtureState {
  pr: PullRequest & { title?: string; commits?: number; auto_merge?: object };
  node: PullRequestNode;
  writes: { kind: string; [field: string]: unknown }[];
  summaries: string[];
  logs: string[];
  approvalError?: unknown;
  mergeError?: unknown;
}

// The github-script globals plus the old validation API some tests attach.
interface FixtureArgs extends GithubScriptArgs {
  github: GithubScriptArgs['github'] & {
    rest: {
      pulls: GithubScriptArgs['github']['rest']['pulls'] & {
        listFiles?: string;
        listCommits?: string;
      };
    };
    paginate?: (method: string) => Promise<unknown[]>;
  };
}

function apiFixture(): { state: FixtureState; args: FixtureArgs } {
  const pr: FixtureState['pr'] = {
    number: 7,
    node_id: 'PR7',
    state: 'open',
    draft: false,
    user: { login: 'dependabot[bot]' },
    base: { ref: 'master' },
    head: { sha: 'head', repo: { full_name: 'owner/repo' } }
  };
  const state: FixtureState = {
    pr,
    node: {
      state: 'OPEN',
      headRefOid: 'head',
      baseRefName: 'master',
      mergeStateStatus: 'BLOCKED',
      autoMergeRequest: null
    },
    writes: [],
    summaries: [],
    logs: []
  };
  const github: FixtureArgs['github'] = {
    rest: {
      pulls: {
        get: async () => ({ data: pr }),
        createReview: async (args) => {
          if (state.approvalError) throw state.approvalError;
          state.writes.push({ kind: 'review', ...args });
        }
      }
    },
    graphql: async (query, variables) => {
      if (query.startsWith('query')) return { node: state.node };
      if (state.mergeError) throw state.mergeError;
      const kind = query.includes('mergePullRequest(')
        ? 'merge'
        : query.includes('enablePullRequestAutoMerge(')
          ? 'enable'
          : assert.fail(`Unexpected mutation: ${query}`);
      assert.match(query, /expectedHeadOid: \$head/);
      assert.match(query, /mergeMethod: SQUASH/);
      state.writes.push({ kind, ...variables });
    }
  };
  return {
    state,
    args: {
      github,
      context: {
        repo: { owner: 'owner', repo: 'repo' },
        payload: { pull_request: structuredClone(pr) }
      },
      core: {
        info(message) {
          state.logs.push(message);
        },
        summary: {
          addRaw(message) {
            state.summaries.push(message);
            return this;
          },
          async write() {}
        }
      }
    }
  };
}

test('Dependabot approval and auto-merge depend only on PR identity, not update metadata', async () => {
  // No file, commit, lockfile or rules APIs are provided: every dependency
  // ecosystem and version change follows the same protected merge path.
  const { state, args } = apiFixture();
  await run(args);
  assert.deepEqual(
    state.writes.map(({ kind }) => kind),
    ['review', 'enable']
  );
  assert.equal(state.writes[0].event, 'APPROVE');
  assert.equal(state.writes[0].commit_id, 'head');
  assert.deepEqual(state.writes[1], {
    kind: 'enable',
    id: 'PR7',
    head: 'head'
  });
  assert.match(state.summaries.join(''), /Auto-merge enabled/);
});

test('Actions major updates and PRs with human branch-update commits are eligible', async () => {
  const { state, args } = apiFixture();
  state.pr.title = 'build(dev): bump actions/checkout from 6 to 7';
  state.pr.commits = 2;
  // Returning these through the old validation API reproduces its rejection.
  args.github.rest.pulls.listFiles = 'files';
  args.github.rest.pulls.listCommits = 'commits';
  args.github.paginate = async (method) => {
    if (method === 'files')
      return [{ filename: '.github/workflows/checks.yml', status: 'modified' }];
    if (method === 'commits') return [{ author: { login: 'maintainer' } }];
    assert.fail('Auto-merge must defer to GitHub branch rules');
  };
  await run(args);
  assert.deepEqual(
    state.writes.map(({ kind }) => kind),
    ['review', 'enable']
  );
});

test('human PRs, forks, other base branches, drafts and closed PRs are skipped', async () => {
  for (const modify of [
    (pr: FixtureState['pr']) => {
      pr.user.login = 'human';
    },
    (pr: FixtureState['pr']) => {
      pr.head.repo!.full_name = 'fork/repo';
    },
    (pr: FixtureState['pr']) => {
      pr.head.repo = null;
    },
    (pr: FixtureState['pr']) => {
      pr.base.ref = 'other';
    },
    (pr: FixtureState['pr']) => {
      pr.draft = true;
    },
    (pr: FixtureState['pr']) => {
      pr.state = 'closed';
    }
  ]) {
    const { state, args } = apiFixture();
    modify(state.pr);
    await run(args);
    assert.deepEqual(state.writes, []);
    assert.match(state.summaries.join(''), /Skipped:/);
  }
});

test('a stale event neither approves nor disables auto-merge for the newer head', async () => {
  const { state, args } = apiFixture();
  state.pr.head.sha = 'new-head';
  state.pr.auto_merge = {};
  await run(args);
  assert.deepEqual(state.writes, []);
  assert.match(state.summaries.join(''), /head changed/);
});

test('changes after approval do not merge a different head or base', async () => {
  for (const change of [
    { headRefOid: 'new-head' },
    { baseRefName: 'other' },
    { state: 'CLOSED' }
  ]) {
    const { state, args } = apiFixture();
    Object.assign(state.node, change);
    await run(args);
    assert.deepEqual(
      state.writes.map(({ kind }) => kind),
      ['review']
    );
    assert.match(state.summaries.join(''), /Skipped:/);
  }
});

test('an immediately mergeable PR uses the protected merge API with the approved head', async () => {
  const { state, args } = apiFixture();
  state.node.mergeStateStatus = 'CLEAN';
  await run(args);
  assert.deepEqual(state.writes[1], { kind: 'merge', id: 'PR7', head: 'head' });
  assert.match(
    state.summaries.join(''),
    /Merged after GitHub branch requirements/
  );
});

test('existing auto-merge is retained and the current head still receives approval', async () => {
  const { state, args } = apiFixture();
  state.node.autoMergeRequest = { enabledAt: '2026-10-26T00:00:00Z' };
  await run(args);
  assert.deepEqual(
    state.writes.map(({ kind }) => kind),
    ['review']
  );
  assert.match(state.summaries.join(''), /already enabled/);
});

test('approval restrictions are visible and leave GitHub to enforce required reviews', async () => {
  for (const status of [403, 409]) {
    const { state, args } = apiFixture();
    state.approvalError = { status };
    await run(args);
    assert.deepEqual(state.writes, [
      { kind: 'enable', id: 'PR7', head: 'head' }
    ]);
    assert.match(
      state.summaries.join(''),
      /Automatic approval was not accepted/
    );
  }
});

test('unexpected approval errors fail the run without enabling auto-merge', async () => {
  const { state, args } = apiFixture();
  state.approvalError = new Error('API unavailable');
  await assert.rejects(run(args), /API unavailable/);
  assert.deepEqual(state.writes, []);
});

test('a rejected merge fails the run without claiming success', async () => {
  const { state, args } = apiFixture();
  state.node.mergeStateStatus = 'CLEAN';
  state.mergeError = new Error('Branch rules rejected merge');
  await assert.rejects(run(args), /Branch rules rejected merge/);
  assert.deepEqual(
    state.writes.map(({ kind }) => kind),
    ['review']
  );
  assert.equal(state.summaries.length, 0);
});

// The workflow loads this module through actions/github-script, which runs its
// script as CommonJS on the action's own Node and imports the .ts file by path.
// The workflow runs from the base branch, so a pull request cannot exercise it.
test('a CommonJS dynamic import loads the module, as github-script does', () => {
  const file = fileURLToPath(
    new URL('./dependabot-auto-merge.ts', import.meta.url)
  );
  const result = spawnSync(
    process.execPath,
    [
      '--input-type=commonjs',
      '-e',
      'import(process.argv[1]).then((m) => process.exit(typeof m.run === "function" ? 0 : 1))',
      file
    ],
    { encoding: 'utf8' }
  );
  assert.equal(result.status, 0, result.stderr);
});
