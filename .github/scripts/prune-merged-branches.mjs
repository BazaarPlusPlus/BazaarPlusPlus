// Deletes branches whose work has already landed through a merged pull
// request. The repository squash-merges, so ancestry cannot tell; a branch
// qualifies only when a merged PR's head commit is still the branch tip.
// Branches with later commits, open PRs (as head or base), the default
// branch, protected branches and release lines are kept.
const RELEASE_LINE = /^\d+\.x$/;

export function keepReason(branch, { defaultBranch, openHeads, openBases }) {
  if (branch.name === defaultBranch) return 'default branch';
  if (branch.protected) return 'protected';
  if (RELEASE_LINE.test(branch.name)) return 'release line';
  if (openHeads.has(branch.name)) return 'open PR';
  if (openBases.has(branch.name)) return 'base of an open PR';
  return null;
}

export async function run({ github, context, core, dryRun }) {
  const repo = context.repo;
  const { data: repository } = await github.rest.repos.get(repo);
  const defaultBranch = repository.default_branch;
  const branches = await github.paginate(github.rest.repos.listBranches, {
    ...repo,
    per_page: 100
  });
  const openPulls = await github.paginate(github.rest.pulls.list, {
    ...repo,
    state: 'open',
    per_page: 100
  });
  const own = `${repo.owner}/${repo.repo}`;
  const openHeads = new Set(
    openPulls
      .filter((pr) => pr.head.repo?.full_name === own)
      .map((pr) => pr.head.ref)
  );
  const openBases = new Set(openPulls.map((pr) => pr.base.ref));

  const rows = [];
  for (const branch of branches) {
    const kept = keepReason(branch, { defaultBranch, openHeads, openBases });
    if (kept) {
      rows.push([branch.name, `kept: ${kept}`]);
      continue;
    }
    const { data: pulls } = await github.rest.pulls.list({
      ...repo,
      state: 'closed',
      head: `${repo.owner}:${branch.name}`,
      per_page: 100
    });
    const merged = pulls.find(
      (pr) => pr.merged_at && pr.head.sha === branch.commit.sha
    );
    if (!merged) {
      rows.push([
        branch.name,
        pulls.some((pr) => pr.merged_at)
          ? 'kept: commits after the merged PR'
          : 'kept: no merged PR'
      ]);
      continue;
    }
    if (dryRun) {
      rows.push([branch.name, `would delete (#${merged.number})`]);
      continue;
    }
    try {
      await github.rest.git.deleteRef({ ...repo, ref: `heads/${branch.name}` });
      rows.push([branch.name, `deleted (#${merged.number})`]);
    } catch (error) {
      // Rulesets can forbid deletion; report it and keep going.
      if (![403, 422].includes(error.status)) throw error;
      core.warning(`Could not delete ${branch.name}: ${error.message}`);
      rows.push([branch.name, `delete refused (${error.status})`]);
    }
  }

  for (const [name, outcome] of rows) core.info(`${name}: ${outcome}`);
  const done = rows.filter(([, outcome]) =>
    /^(deleted|would delete)/.test(outcome)
  ).length;
  await core.summary
    .addHeading(dryRun ? 'Merged branches (dry run)' : 'Merged branches')
    .addRaw(
      `${done} of ${rows.length} branches ${dryRun ? 'would be deleted' : 'deleted'}.\n\n`
    )
    .addTable([
      [
        { data: 'Branch', header: true },
        { data: 'Outcome', header: true }
      ],
      ...rows
    ])
    .write();
  return rows;
}
