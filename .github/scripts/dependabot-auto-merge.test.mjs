import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import test from 'node:test';
import {
  documentsArePatchOnly,
  eligibleFiles,
  patchOnly,
  protectionReady,
  REQUIRED_CHECKS,
  run
} from './dependabot-auto-merge.mjs';

function fixture() {
  const beforeManifest = {
    name: 'test',
    devDependencies: { library: '^1.2.3' },
    scripts: { test: 'vitest run' }
  };
  const beforeLock = {
    lockfileVersion: 3,
    packages: {
      '': { ...beforeManifest },
      'node_modules/library': {
        version: '1.2.3',
        resolved: 'https://registry.npmjs.org/library/-/library-1.2.3.tgz',
        integrity: 'sha512-old',
        dev: true
      }
    }
  };
  const afterManifest = structuredClone(beforeManifest);
  afterManifest.devDependencies.library = '^1.2.4';
  const afterLock = structuredClone(beforeLock);
  afterLock.packages[''].devDependencies.library = '^1.2.4';
  Object.assign(afterLock.packages['node_modules/library'], {
    version: '1.2.4',
    resolved: 'https://registry.npmjs.org/library/-/library-1.2.4.tgz',
    integrity: 'sha512-new'
  });
  return [beforeManifest, afterManifest, beforeLock, afterLock];
}

function rules() {
  return [
    {
      type: 'pull_request',
      parameters: {
        required_approving_review_count: 1,
        dismiss_stale_reviews_on_push: true
      }
    },
    {
      type: 'required_status_checks',
      parameters: {
        strict_required_status_checks_policy: true,
        required_status_checks: REQUIRED_CHECKS.map((context) => ({
          context,
          integration_id: 15368
        }))
      }
    }
  ];
}

test('accepts a stable patch and does not mutate the input snapshots', () => {
  const data = fixture();
  const copy = structuredClone(data);
  assert.equal(patchOnly(...data), true);
  assert.deepEqual(data, copy);
});

test('minor, major, prerelease, downgrade and range policy changes need human review', () => {
  for (const version of ['1.3.0', '2.0.0', '1.2.4-rc.1', '1.2.2', '1.2.3']) {
    const data = fixture();
    data[3].packages['node_modules/library'].version = version;
    assert.equal(patchOnly(...data), false, version);
  }
  const data = fixture();
  data[1].devDependencies.library = '~1.2.4';
  assert.equal(patchOnly(...data), false);
});

test('scripts, overrides, engines, new packages and changed install hooks cannot hide in a patch', () => {
  for (const modify of [
    (data) => {
      data[1].scripts.test = 'different command';
    },
    (data) => {
      data[1].overrides = { other: '1.0.0' };
    },
    (data) => {
      data[1].engines = { node: '>=26' };
    },
    (data) => {
      data[3].packages['node_modules/added'] = { version: '1.0.0' };
    },
    (data) => {
      data[3].packages['node_modules/library'].hasInstallScript = true;
    },
    (data) => {
      data[3].packages['node_modules/library'].resolved =
        'https://example.com/library.tgz';
    },
    (data) => {
      data[3].packages['node_modules/library'].dependencies = {
        nested: '2.0.0'
      };
    }
  ]) {
    const data = fixture();
    modify(data);
    assert.equal(patchOnly(...data), false);
  }
});

test('external npm, Cargo and uv dependency files are eligible; no cross-project, source, workflow or removed files', () => {
  for (const path of [
    'package-lock.json',
    'bazaarplusplus-site/package-lock.json',
    'bazaarplusplus-installer/package-lock.json',
    'bazaarplusplus-installer/src-tauri/Cargo.lock',
    'bazaarplusplus-server/package-lock.json',
    'bazaarplusplus-analyzer/uv.lock'
  ]) {
    assert.ok(eligibleFiles([{ filename: path, status: 'modified' }]));
  }
  for (const path of [
    'bazaarplusplus-mod/Directory.Packages.props',
    'bazaarplusplus-installer/src-tauri/tauri.conf.json',
    'bazaarplusplus-installer/src-tauri/build.rs',
    'bazaarplusplus-server/contracts/v5/bundle.proto',
    '.github/workflows/checks.yml',
    'bazaarplusplus-site/src/main.tsx'
  ]) {
    assert.equal(eligibleFiles([{ filename: path, status: 'modified' }]), null);
  }
  assert.equal(
    eligibleFiles([{ filename: 'package-lock.json', status: 'removed' }]),
    null
  );
  assert.equal(
    eligibleFiles(
      ['package-lock.json', 'bazaarplusplus-site/package.json'].map(
        (filename) => ({ filename, status: 'modified' })
      )
    ),
    null
  );
  assert.equal(eligibleFiles([]), null);
});

test('strict required checks, trusted app and stale-review dismissal are mandatory', () => {
  assert.equal(protectionReady(rules()), true);
  assert.equal(protectionReady([]), false);
  for (const modify of [
    (value) => {
      value[0].parameters.required_approving_review_count = 0;
    },
    (value) => {
      value[0].parameters.dismiss_stale_reviews_on_push = false;
    },
    (value) => {
      value[1].parameters.strict_required_status_checks_policy = false;
    },
    (value) => {
      value[1].parameters.required_status_checks.pop();
    },
    (value) => {
      value[1].parameters.required_status_checks[0].integration_id = null;
    }
  ]) {
    const value = rules();
    modify(value);
    assert.equal(protectionReady(value), false);
  }
});

function apiFixture() {
  const data = fixture();
  const pr = {
    number: 7,
    node_id: 'PR7',
    commits: 1,
    changed_files: 2,
    state: 'open',
    user: { login: 'dependabot[bot]' },
    base: { ref: 'master', sha: 'base' },
    head: { sha: 'head', repo: { full_name: 'owner/repo' } }
  };
  const writes = [];
  const state = {
    rules: rules(),
    commits: [
      {
        sha: 'head',
        author: { login: 'dependabot[bot]' },
        commit: { verification: { verified: true } }
      }
    ],
    pr,
    current: pr,
    mergeStateStatus: 'BLOCKED',
    writes
  };
  let reads = 0;
  const github = {
    paginate: async (method) => {
      if (method === 'files')
        return [
          { filename: 'package-lock.json', status: 'modified' },
          { filename: 'package.json', status: 'modified' }
        ];
      if (method === 'commits') return state.commits;
      return state.rules;
    },
    rest: {
      pulls: {
        get: async () => ({ data: reads++ ? state.current : pr }),
        listFiles: 'files',
        listCommits: 'commits',
        createReview: async (args) => {
          if (state.approvalError) throw state.approvalError;
          writes.push({ kind: 'review', ...args });
        }
      },
      repos: {
        compareCommitsWithBasehead: async () => ({
          data: { merge_base_commit: { sha: 'base' } }
        }),
        getContent: async ({ ref, path }) => ({
          data: {
            type: 'file',
            encoding: 'base64',
            content: Buffer.from(
              JSON.stringify(
                data[
                  (path === 'package-lock.json' ? 2 : 0) +
                    (ref === 'head' ? 1 : 0)
                ]
              )
            ).toString('base64')
          }
        })
      }
    },
    graphql: async (query, variables) => {
      if (query.startsWith('query'))
        return {
          node: {
            state: 'OPEN',
            headRefOid: 'head',
            baseRefName: 'master',
            mergeStateStatus: state.mergeStateStatus
          }
        };
      writes.push({
        kind: query.includes('disablePullRequest')
          ? 'disable'
          : query.includes('mergePullRequest(')
            ? 'merge'
            : 'enable',
        ...variables
      });
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
      core: { info() {} }
    }
  };
}

test('approval and native auto-merge both refer to the inspected commit', async () => {
  const { state, args } = apiFixture();
  await run(args);
  assert.equal(state.writes[0].commit_id, 'head');
  assert.deepEqual(state.writes[1], {
    kind: 'enable',
    id: 'PR7',
    head: 'head'
  });
});

test('missing protection, unsigned commits and a changing head never receive approval', async () => {
  for (const modify of [
    (state) => {
      state.rules = [];
    },
    (state) => {
      state.commits[0].commit.verification.verified = false;
    },
    (state) => {
      state.commits[0].author.login = 'human';
    },
    (state) => {
      state.pr.commits = 251;
    },
    (state) => {
      state.pr.commits = 2;
    },
    (state) => {
      state.commits[0].sha = 'other-head';
    },
    (state) => {
      state.current = {
        ...state.pr,
        head: { ...state.pr.head, sha: 'new-head' }
      };
    }
  ]) {
    const { state, args } = apiFixture();
    modify(state);
    await run(args);
    assert.deepEqual(state.writes, []);
  }
});

test('an immediately mergeable PR uses the protected merge API with the same head', async () => {
  const { state, args } = apiFixture();
  state.mergeStateStatus = 'CLEAN';
  await run(args);
  assert.deepEqual(state.writes[1], { kind: 'merge', id: 'PR7', head: 'head' });
});

test('organization approval restrictions preserve human approval before auto-merge', async () => {
  const { state, args } = apiFixture();
  state.approvalError = { status: 403 };
  await run(args);
  assert.deepEqual(state.writes, [{ kind: 'enable', id: 'PR7', head: 'head' }]);
});

test('an already enabled PR is disarmed if protection no longer qualifies', async () => {
  const { state, args } = apiFixture();
  state.pr.auto_merge = {};
  state.rules = [];
  await run(args);
  assert.deepEqual(state.writes, [{ kind: 'disable', id: 'PR7' }]);
});

// A required check that no workflow job can report blocks every merge, so each
// name must be a job's display name (job id when it has none) in .github/workflows.
// A matrix name such as "Installer (${{ matrix.os }})" is matched as a pattern.
test('every required check is a job name declared in the workflows', () => {
  const workflows = path.join(import.meta.dirname, '..', 'workflows');
  const patterns = fs
    .readdirSync(workflows)
    .filter((file) => file.endsWith('.yml'))
    .flatMap((file) => {
      const text = fs.readFileSync(path.join(workflows, file), 'utf8');
      const jobs = text.slice(text.indexOf('\njobs:\n') + 1);
      return [
        ...jobs.matchAll(
          /^  ([\w-]+):\n(?:    (?!name:)[^\n]*\n)*?(?:    name: ([^\n]+)\n)?/gm
        )
      ].map(
        ([, id, name]) =>
          new RegExp(
            `^${(name ?? id)
              .split(/\$\{\{[^}]*\}\}/)
              .map((literal) => literal.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'))
              .join('.+')}$`
          )
      );
    });
  assert.ok(patterns.length >= 8, 'job names were parsed');
  for (const name of REQUIRED_CHECKS)
    assert.ok(
      patterns.some((pattern) => pattern.test(name)),
      `${name} is not a job in .github/workflows`
    );
  assert.ok(
    !REQUIRED_CHECKS.includes('Mod pure logic (no game compatibility)')
  );
});

test('both native installer gates must be required before any automatic merge', () => {
  for (const os of ['macos-14', 'windows-latest']) {
    const value = rules();
    value[1].parameters.required_status_checks =
      value[1].parameters.required_status_checks.filter(
        ({ context }) => context !== `Installer (${os})`
      );
    assert.equal(protectionReady(value), false);
  }
});

test('trusted TOML parser accepts Cargo patches and fails closed on malformed data', () => {
  const manifest = '[dependencies]\nlibrary = "1.2.3"\n';
  const lock = `version = 4
[[package]]
name = "library"
version = "1.2.3"
source = "registry+https://github.com/rust-lang/crates.io-index"
checksum = "${'a'.repeat(64)}"
`;
  assert.equal(
    documentsArePatchOnly('cargo', [
      manifest,
      manifest.replace('1.2.3', '1.2.4'),
      lock,
      lock.replace('1.2.3', '1.2.4')
    ]),
    true
  );
  assert.equal(documentsArePatchOnly('uv', ['[invalid', '', '', '']), false);
});
