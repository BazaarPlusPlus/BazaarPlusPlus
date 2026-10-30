import assert from 'node:assert/strict';
import test from 'node:test';
import { run } from './prune-merged-branches.mjs';

function apiFixture() {
  const state = {
    branches: [
      { name: 'master', protected: true, commit: { sha: 'm' } },
      { name: '5.x', protected: false, commit: { sha: 'r' } },
      { name: 'merged', protected: false, commit: { sha: 'a' } },
      { name: 'later-commits', protected: false, commit: { sha: 'b2' } },
      { name: 'unmerged', protected: false, commit: { sha: 'c' } },
      { name: 'open', protected: false, commit: { sha: 'd' } },
      { name: 'stack-base', protected: false, commit: { sha: 'e' } }
    ],
    openPulls: [
      {
        head: { ref: 'open', repo: { full_name: 'owner/repo' } },
        base: { ref: 'master' }
      },
      {
        head: { ref: 'stacked', repo: { full_name: 'owner/repo' } },
        base: { ref: 'stack-base' }
      }
    ],
    closedPulls: {
      '5.x': [{ number: 1, merged_at: 't', head: { sha: 'r' } }],
      merged: [
        { number: 2, merged_at: null, head: { sha: 'old' } },
        { number: 3, merged_at: 't', head: { sha: 'a' } }
      ],
      'later-commits': [{ number: 4, merged_at: 't', head: { sha: 'b1' } }],
      unmerged: [{ number: 5, merged_at: null, head: { sha: 'c' } }],
      'stack-base': [{ number: 6, merged_at: 't', head: { sha: 'e' } }]
    },
    deleted: [],
    deleteError: null,
    summaries: []
  };
  const listBranches = 'listBranches';
  const listPulls = async ({ state: prState, head }) => {
    assert.equal(prState, 'closed');
    assert.match(head, /^owner:/);
    return { data: state.closedPulls[head.slice('owner:'.length)] ?? [] };
  };
  const github = {
    paginate: async (method, args) => {
      if (method === listBranches) return state.branches;
      assert.equal(method, listPulls);
      assert.equal(args.state, 'open');
      return state.openPulls;
    },
    rest: {
      repos: {
        get: async () => ({ data: { default_branch: 'master' } }),
        listBranches
      },
      pulls: { list: listPulls },
      git: {
        deleteRef: async ({ ref }) => {
          if (state.deleteError) throw state.deleteError;
          state.deleted.push(ref);
        }
      }
    }
  };
  const summary = {
    addHeading(text) {
      state.summaries.push(text);
      return this;
    },
    addRaw(text) {
      state.summaries.push(text);
      return this;
    },
    addTable(rows) {
      state.summaries.push(JSON.stringify(rows));
      return this;
    },
    async write() {}
  };
  const core = { info() {}, warning() {}, summary };
  return {
    state,
    args: { github, context: { repo: { owner: 'owner', repo: 'repo' } }, core }
  };
}

test('only branches whose tip is a merged PR head are deleted', async () => {
  const { state, args } = apiFixture();
  const rows = await run({ ...args, dryRun: false });
  assert.deepEqual(state.deleted, ['heads/merged']);
  assert.deepEqual(Object.fromEntries(rows), {
    master: 'kept: default branch',
    '5.x': 'kept: release line',
    merged: 'deleted (#3)',
    'later-commits': 'kept: commits after the merged PR',
    unmerged: 'kept: no merged PR',
    open: 'kept: open PR',
    'stack-base': 'kept: base of an open PR'
  });
});

test('dry run reports without deleting', async () => {
  const { state, args } = apiFixture();
  const rows = await run({ ...args, dryRun: true });
  assert.deepEqual(state.deleted, []);
  assert.equal(Object.fromEntries(rows).merged, 'would delete (#3)');
  assert.match(state.summaries.join(''), /dry run/);
});

test('protected branches are kept even when merged', async () => {
  const { state, args } = apiFixture();
  state.branches.find((b) => b.name === 'merged').protected = true;
  const rows = await run({ ...args, dryRun: false });
  assert.equal(Object.fromEntries(rows).merged, 'kept: protected');
  assert.deepEqual(state.deleted, []);
});

test('an open fork PR with the same head name does not shield a branch', async () => {
  const { state, args } = apiFixture();
  state.openPulls.push({
    head: { ref: 'merged', repo: { full_name: 'fork/repo' } },
    base: { ref: 'master' }
  });
  await run({ ...args, dryRun: false });
  assert.deepEqual(state.deleted, ['heads/merged']);
});

test('a refused deletion is reported and the run continues', async () => {
  const { state, args } = apiFixture();
  state.deleteError = Object.assign(new Error('rule'), { status: 422 });
  const rows = await run({ ...args, dryRun: false });
  assert.equal(Object.fromEntries(rows).merged, 'delete refused (422)');
});

test('unexpected API errors fail the run', async () => {
  const { state, args } = apiFixture();
  state.deleteError = Object.assign(new Error('boom'), { status: 500 });
  await assert.rejects(run({ ...args, dryRun: false }), /boom/);
});
