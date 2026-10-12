import type { Env } from "../env";
import { logError, logEvent } from "../observability";

/**
 * The hourly Cron Trigger that starts the analyzer GitHub Actions workflow.
 * GitHub drops most `schedule` events under load; Cloudflare Cron Triggers do
 * not, so this Worker owns the analyzer's hourly cadence. Must match a
 * `[triggers].crons` entry in wrangler.toml.
 */
export const ANALYZER_DISPATCH_CRON = "23 * * * *";

export const ANALYZER_WORKFLOW_DISPATCH_URL =
  "https://api.github.com/repos/BazaarPlusPlus/BazaarPlusPlus/actions/workflows/analyzer.yml/dispatches";

const DISPATCH_TIMEOUT_MS = 10_000;

export type AnalyzerDispatchOutcome = "dispatched" | "skipped" | "failed";

/**
 * Requests one analyzer run on master with publishing enabled. A missing
 * secret or a GitHub failure is logged and never fails the invocation: the
 * analyzer backfills every missed hour on its next run.
 */
export async function dispatchAnalyzer(
  env: Pick<Env, "GITHUB_ANALYZER_DISPATCH_TOKEN">,
  scheduledAtMs: number,
): Promise<AnalyzerDispatchOutcome> {
  const credential = env.GITHUB_ANALYZER_DISPATCH_TOKEN;
  if (typeof credential !== "string" || credential.length === 0) {
    logEvent("analyzer.dispatch.skipped", {
      scheduled_at_ms: scheduledAtMs,
      reason: "missing_credential",
    });
    return "skipped";
  }

  let status: number | undefined;
  try {
    const response = await fetch(ANALYZER_WORKFLOW_DISPATCH_URL, {
      method: "POST",
      headers: {
        Accept: "application/vnd.github+json",
        Authorization: `Bearer ${credential}`,
        "Content-Type": "application/json",
        "User-Agent": "bazaarplusplus-mod-api-v6",
        "X-GitHub-Api-Version": "2022-11-28",
      },
      body: JSON.stringify({ ref: "master", inputs: { publish: "true" } }),
      signal: AbortSignal.timeout(DISPATCH_TIMEOUT_MS),
    });
    status = response.status;
    await response.body?.cancel();
  } catch (error) {
    logError("analyzer.dispatch.failed", {
      scheduled_at_ms: scheduledAtMs,
      error: error instanceof Error ? error.name : "unknown",
    });
    return "failed";
  }

  if (status < 200 || status >= 300) {
    logError("analyzer.dispatch.failed", { scheduled_at_ms: scheduledAtMs, status });
    return "failed";
  }
  logEvent("analyzer.dispatch", { scheduled_at_ms: scheduledAtMs, status });
  return "dispatched";
}
