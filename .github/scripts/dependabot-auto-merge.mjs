import { isDeepStrictEqual } from 'node:util';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

// These are job names, not workflow names. Require GitHub Actions as their source.
// A name joins this list only after the GitHub ruleset requires it, and the
// ruleset only after the job has run green once (docs/development.md).
export const REQUIRED_CHECKS = [
  'Checks summary',
  'Bot Mod dependency boundary',
  'dependencies',
  'CodeQL (javascript-typescript)',
  'CodeQL (python)'
];

export function protectionReady(rules) {
  const reviewed = rules.some(
    (rule) =>
      rule.type === 'pull_request' &&
      rule.parameters.required_approving_review_count >= 1 &&
      rule.parameters.dismiss_stale_reviews_on_push === true
  );
  const strictChecks = rules
    .filter(
      (rule) =>
        rule.type === 'required_status_checks' &&
        rule.parameters.strict_required_status_checks_policy === true &&
        rule.parameters.do_not_enforce_on_create !== true
    )
    .flatMap((rule) => rule.parameters.required_status_checks);
  return (
    reviewed &&
    REQUIRED_CHECKS.every((name) =>
      strictChecks.some(
        (check) => check.context === name && check.integration_id === 15368
      )
    )
  );
}

function patchVersion(before, after) {
  const parse = (value) =>
    typeof value === 'string' &&
    value.match(/^(\^|~)?(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$/);
  const a = parse(before);
  const b = parse(after);
  return (
    a &&
    b &&
    a[1] === b[1] &&
    a[2] === b[2] &&
    a[3] === b[3] &&
    BigInt(b[4]) > BigInt(a[4])
  );
}

function normalizeRanges(before, after) {
  for (const field of [
    'dependencies',
    'devDependencies',
    'optionalDependencies',
    'peerDependencies'
  ]) {
    for (const [name, oldVersion] of Object.entries(before[field] ?? {})) {
      const newVersion = after[field]?.[name];
      if (patchVersion(oldVersion, newVersion)) after[field][name] = oldVersion;
    }
  }
}

export function eligibleFiles(files) {
  if (!files.length || files.some((file) => file.status !== 'modified'))
    return null;
  const candidates = [
    '',
    'bazaarplusplus-site/',
    'bazaarplusplus-server/',
    'bazaarplusplus-installer/'
  ].map((directory) => ({
    kind: 'npm',
    manifest: `${directory}package.json`,
    lock: `${directory}package-lock.json`
  }));
  candidates.push(
    {
      kind: 'cargo',
      manifest: 'bazaarplusplus-installer/src-tauri/Cargo.toml',
      lock: 'bazaarplusplus-installer/src-tauri/Cargo.lock'
    },
    {
      kind: 'uv',
      manifest: 'bazaarplusplus-analyzer/pyproject.toml',
      lock: 'bazaarplusplus-analyzer/uv.lock'
    }
  );
  for (const candidate of candidates) {
    const { manifest, lock } = candidate;
    if (
      files.some((file) => file.filename === lock) &&
      files.every((file) => [manifest, lock].includes(file.filename))
    ) {
      return candidate;
    }
  }
  return null;
}

export function documentsArePatchOnly(kind, documents) {
  if (kind === 'npm')
    return patchOnly(...documents.map((text) => JSON.parse(text)));
  return JSON.parse(
    execFileSync(
      'python3',
      [
        fileURLToPath(
          new URL('./check_toml_dependency_update.py', import.meta.url)
        )
      ],
      {
        input: JSON.stringify({ kind, documents }),
        encoding: 'utf8',
        timeout: 15000,
        maxBuffer: 1024 * 1024
      }
    )
  );
}

export function patchOnly(
  beforeManifest,
  afterManifest,
  beforeLock,
  afterLock
) {
  const manifest = structuredClone(afterManifest);
  normalizeRanges(beforeManifest, manifest);
  if (!isDeepStrictEqual(beforeManifest, manifest)) return false;
  if (
    beforeLock.lockfileVersion !== 3 ||
    afterLock.lockfileVersion !== 3 ||
    !beforeLock.packages ||
    !afterLock.packages
  )
    return false;
  const lock = structuredClone(afterLock);
  let patches = 0;
  for (const [path, before] of Object.entries(beforeLock.packages)) {
    const after = lock.packages[path];
    if (!after) return false;
    normalizeRanges(before, after);
    if (path && patchVersion(before.version, after.version)) {
      // No Git/tarball/local replacements, even when version strings look safe.
      if (
        !before.resolved?.startsWith('https://registry.npmjs.org/') ||
        !after.resolved?.startsWith('https://registry.npmjs.org/') ||
        typeof after.integrity !== 'string' ||
        !after.integrity.startsWith('sha512-')
      )
        return false;
      after.version = before.version;
      after.resolved = before.resolved;
      after.integrity = before.integrity;
      patches++;
    }
  }
  // New/removed packages, changed scripts/engines/overrides and graph reshuffles
  // all require human review. A patch label alone never grants approval.
  return patches > 0 && isDeepStrictEqual(beforeLock, lock);
}

export async function run({ github, context, core }) {
  const repo = context.repo;
  const pull_number = context.payload.pull_request.number;
  const { data: pr } = await github.rest.pulls.get({ ...repo, pull_number });
  const decline = async (reason) => {
    core.info(`Manual review: ${reason}`);
    if (pr.auto_merge)
      await github.graphql(
        'mutation($id: ID!) { disablePullRequestAutoMerge(input: {pullRequestId: $id}) { clientMutationId } }',
        { id: pr.node_id }
      );
  };
  if (pr.state !== 'open' || pr.draft) return;
  if (
    pr.user.login !== 'dependabot[bot]' ||
    pr.base.ref !== 'master' ||
    pr.head.repo?.full_name !== `${repo.owner}/${repo.repo}`
  )
    return decline('untrusted PR origin or base');
  if (pr.head.sha !== context.payload.pull_request.head.sha)
    return decline('head changed; wait for the new run');
  const files = await github.paginate(github.rest.pulls.listFiles, {
    ...repo,
    pull_number,
    per_page: 100
  });
  const paths = eligibleFiles(files);
  if (!paths || files.length !== pr.changed_files)
    return decline(
      'only external application dependency manifests and lockfiles are eligible'
    );
  const commits = await github.paginate(github.rest.pulls.listCommits, {
    ...repo,
    pull_number,
    per_page: 100
  });
  if (
    !commits.length ||
    pr.commits > 250 ||
    commits.length !== pr.commits ||
    !commits.some((commit) => commit.sha === pr.head.sha) ||
    !commits.every(
      (commit) =>
        commit.author?.login === 'dependabot[bot]' &&
        commit.commit.verification?.verified
    )
  )
    return decline('every commit must have a verified Dependabot signature');
  const { data: comparison } =
    await github.rest.repos.compareCommitsWithBasehead({
      ...repo,
      basehead: `${pr.base.sha}...${pr.head.sha}`
    });
  const readText = async (ref, path) => {
    const { data } = await github.rest.repos.getContent({ ...repo, ref, path });
    if (data.type !== 'file' || data.encoding !== 'base64')
      throw new Error(`Cannot read ${path}`);
    return Buffer.from(data.content, 'base64').toString('utf8');
  };
  const documents = await Promise.all([
    readText(comparison.merge_base_commit.sha, paths.manifest),
    readText(pr.head.sha, paths.manifest),
    readText(comparison.merge_base_commit.sha, paths.lock),
    readText(pr.head.sha, paths.lock)
  ]);
  if (!documentsArePatchOnly(paths.kind, documents))
    return decline('dependency graph is not a stable patch-only update');
  const rules = await github.paginate(
    'GET /repos/{owner}/{repo}/rules/branches/{branch}',
    { ...repo, branch: 'master', per_page: 100 }
  );
  if (!protectionReady(rules))
    return decline(
      'waiting for strict required CI checks and stale-review dismissal to be configured'
    );
  // Recheck after API reads; approval and auto-merge are bound to the inspected head.
  const { data: current } = await github.rest.pulls.get({
    ...repo,
    pull_number
  });
  if (
    current.head.sha !== pr.head.sha ||
    current.base.ref !== 'master' ||
    current.state !== 'open'
  )
    return decline('PR changed during validation');
  try {
    await github.rest.pulls.createReview({
      ...repo,
      pull_number,
      commit_id: pr.head.sha,
      event: 'APPROVE',
      body: 'Verified Dependabot patch-only update to external application dependencies. Mod/game assemblies are excluded. GitHub required checks must pass before merging.'
    });
  } catch (error) {
    if (![403, 409].includes(error.status)) throw error;
    core.info(
      'Actions approval is not permitted; auto-merge will wait for human approval.'
    );
  }
  const { node } = await github.graphql(
    'query($id: ID!) { node(id: $id) { ... on PullRequest { state headRefOid baseRefName mergeStateStatus autoMergeRequest { enabledAt } } } }',
    { id: pr.node_id }
  );
  if (node.state !== 'OPEN') return;
  if (node.headRefOid !== pr.head.sha || node.baseRefName !== 'master')
    return decline('PR changed after approval');
  if (node.autoMergeRequest) return;
  if (node.mergeStateStatus === 'CLEAN') {
    await github.graphql(
      'mutation($id: ID!, $head: GitObjectID!) { mergePullRequest(input: {pullRequestId: $id, expectedHeadOid: $head, mergeMethod: SQUASH}) { clientMutationId } }',
      { id: pr.node_id, head: pr.head.sha }
    );
    core.info('Merged after GitHub protection requirements were satisfied.');
    return;
  }
  await github.graphql(
    'mutation($id: ID!, $head: GitObjectID!) { enablePullRequestAutoMerge(input: {pullRequestId: $id, expectedHeadOid: $head, mergeMethod: SQUASH}) { clientMutationId } }',
    { id: pr.node_id, head: pr.head.sha }
  );
  core.info('Native auto-merge enabled; required checks remain enforced.');
}
