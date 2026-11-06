// The REST pull request fields this script reads.
export interface PullRequest {
  number: number;
  node_id: string;
  state: string;
  draft?: boolean;
  user: { login: string };
  base: { ref: string };
  head: { sha: string; repo: { full_name: string } | null };
}

// The GraphQL PullRequest fields this script queries.
export interface PullRequestNode {
  state: string;
  headRefOid: string;
  baseRefName: string;
  mergeStateStatus: string;
  autoMergeRequest: { enabledAt: string } | null;
}

// The actions/core job summary; addRaw returns the summary for chaining.
export interface CoreSummary {
  addRaw(text: string): CoreSummary;
  write(): Promise<unknown>;
}

export interface RepositoryRef {
  owner: string;
  repo: string;
}

// The parts of the actions/github-script globals this script uses.
export interface GithubScriptArgs {
  github: {
    rest: {
      pulls: {
        get(
          params: RepositoryRef & { pull_number: number }
        ): Promise<{ data: PullRequest }>;
        createReview(
          params: RepositoryRef & {
            pull_number: number;
            commit_id: string;
            event: string;
            body: string;
          }
        ): Promise<unknown>;
      };
    };
    graphql(query: string, variables: Record<string, string>): Promise<unknown>;
  };
  context: {
    repo: RepositoryRef;
    payload: { pull_request: { number: number; head: { sha: string } } };
  };
  core: {
    info(message: string): void;
    summary: CoreSummary;
  };
}

export async function run({
  github,
  context,
  core
}: GithubScriptArgs): Promise<void> {
  const repo = context.repo;
  const pull_number = context.payload.pull_request.number;
  const { data: pr } = await github.rest.pulls.get({ ...repo, pull_number });
  const report = async (message: string) => {
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
    if (![403, 409].includes((error as { status: number }).status)) throw error;
    await report(
      'Automatic approval was not accepted; GitHub still requires the configured approvals.'
    );
  }
  const { node } = (await github.graphql(
    'query($id: ID!) { node(id: $id) { ... on PullRequest { state headRefOid baseRefName mergeStateStatus autoMergeRequest { enabledAt } } } }',
    { id: pr.node_id }
  )) as { node: PullRequestNode };
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
