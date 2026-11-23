import { logError, logEvent } from '../observability';

// GitHub drops or delays many scheduled workflow runs, so this Worker's Cron
// Triggers start these workflows instead (docs/adr/0005-workflow-dispatch.md). Every key is also a
// `crons` entry in wrangler.toml.
export const WORKFLOW_DISPATCHES: Readonly<
  Record<string, { workflow: string; inputs: Record<string, string> }>
> = {
  // Seals the previous UTC day so the report's own analyzer run is short.
  '0 1 * * *': { workflow: 'analyzer.yml', inputs: {} },
  '0 12 * * *': { workflow: 'analyzer.yml', inputs: {} },
  // Reaches the QQ groups near 10:00 Beijing time.
  '50 1 * * *': { workflow: 'daily-report.yml', inputs: { send: 'report' } }
};

const WORKFLOWS_URL =
  'https://api.github.com/repos/BazaarPlusPlus/BazaarPlusPlus/actions/workflows';

export async function dispatchWorkflow(
  token: string | undefined,
  dispatch: { workflow: string; inputs: Record<string, string> },
  scheduledAtMs: number
): Promise<void> {
  const fields = {
    workflow: dispatch.workflow,
    scheduled_at_ms: scheduledAtMs
  };
  if (!token) {
    logError('workflow.dispatch.failed', {
      ...fields,
      reason: 'GITHUB_DISPATCH_TOKEN is not set'
    });
    throw new Error('GITHUB_DISPATCH_TOKEN is not set');
  }
  let response: Response;
  try {
    response = await fetch(`${WORKFLOWS_URL}/${dispatch.workflow}/dispatches`, {
      method: 'POST',
      headers: {
        accept: 'application/vnd.github+json',
        authorization: `Bearer ${token}`,
        'content-type': 'application/json',
        // GitHub rejects API requests without a User-Agent.
        'user-agent': 'bazaarplusplus-mod-api-v6'
      },
      body: JSON.stringify({ ref: 'master', inputs: dispatch.inputs })
    });
  } catch (error) {
    logError('workflow.dispatch.failed', {
      ...fields,
      reason: 'request failed'
    });
    throw error;
  }
  if (!response.ok) {
    logError('workflow.dispatch.failed', {
      ...fields,
      status: response.status
    });
    throw new Error(`${dispatch.workflow} dispatch failed: ${response.status}`);
  }
  logEvent('workflow.dispatch', fields);
}
