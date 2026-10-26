export async function run({ github, context, core }) {
  const repo = context.repo;
  const pull_number = context.payload.pull_request.number;
  const { data: pr } = await github.rest.pulls.get({ ...repo, pull_number });
  const report = async (message) => {
    core.info(message);
    await core.summary.addRaw(`${message}\n\n`).write();
  };
  if (pr.state !== 'open' || pr.draft)
    return report('Skipped: PR is closed or still a draft.');
  if (
    pr.user.login !== 'dependabot[bot]' ||
    pr.base.ref !== 'master' ||
    pr.head.repo?.full_name !== `${repo.owner}/${repo.repo}`
  )
    return report(
      'Skipped: PR is not a repository Dependabot update to master.'
    );
  if (pr.head.sha !== context.payload.pull_request.head.sha)
    return report('Skipped: head changed; the new event will handle this PR.');
  try {
    await github.rest.pulls.createReview({
      ...repo,
      pull_number,
      commit_id: pr.head.sha,
      event: 'APPROVE',
      body: 'Approve this Dependabot update. GitHub branch rules and required checks must pass before merging.'
    });
  } catch (error) {
    if (![403, 409].includes(error.status)) throw error;
    await report(
      'Automatic approval was not accepted; GitHub still requires the configured approvals.'
    );
  }
  const { node } = await github.graphql(
    'query($id: ID!) { node(id: $id) { ... on PullRequest { state headRefOid baseRefName mergeStateStatus autoMergeRequest { enabledAt } } } }',
    { id: pr.node_id }
  );
  if (node.state !== 'OPEN') return report('Skipped: PR is no longer open.');
  if (node.headRefOid !== pr.head.sha || node.baseRefName !== 'master')
    return report(
      'Skipped: PR changed after approval; waiting for the new event.'
    );
  if (node.autoMergeRequest)
    return report(
      'Auto-merge is already enabled; waiting for GitHub branch requirements.'
    );
  if (node.mergeStateStatus === 'CLEAN') {
    await github.graphql(
      'mutation($id: ID!, $head: GitObjectID!) { mergePullRequest(input: {pullRequestId: $id, expectedHeadOid: $head, mergeMethod: SQUASH}) { clientMutationId } }',
      { id: pr.node_id, head: pr.head.sha }
    );
    return report('Merged after GitHub branch requirements were satisfied.');
  }
  await github.graphql(
    'mutation($id: ID!, $head: GitObjectID!) { enablePullRequestAutoMerge(input: {pullRequestId: $id, expectedHeadOid: $head, mergeMethod: SQUASH}) { clientMutationId } }',
    { id: pr.node_id, head: pr.head.sha }
  );
  await report('Auto-merge enabled; waiting for GitHub branch requirements.');
}
