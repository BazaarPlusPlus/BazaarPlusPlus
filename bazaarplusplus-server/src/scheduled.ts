import type { Env } from "./env";
import { ANALYZER_DISPATCH_CRON, dispatchAnalyzer } from "./modules/analyzer-dispatch";
import { pruneExpiredBundles } from "./modules/d1-retention";

export async function scheduled(controller: ScheduledController, env: Env): Promise<void> {
  if (controller.cron === ANALYZER_DISPATCH_CRON) {
    await dispatchAnalyzer(env, controller.scheduledTime);
    return;
  }
  await pruneExpiredBundles(env.DB, controller.scheduledTime);
}
