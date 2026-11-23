import type { Env } from './env';
import { pruneExpiredBundles } from './modules/d1-retention';
import {
  WORKFLOW_DISPATCHES,
  dispatchWorkflow
} from './modules/workflow-dispatch';

export async function scheduled(
  controller: ScheduledController,
  env: Env
): Promise<void> {
  const dispatch = Object.hasOwn(WORKFLOW_DISPATCHES, controller.cron)
    ? WORKFLOW_DISPATCHES[controller.cron]
    : undefined;
  if (dispatch) {
    await dispatchWorkflow(
      env.GITHUB_DISPATCH_TOKEN,
      dispatch,
      controller.scheduledTime
    );
    return;
  }
  await pruneExpiredBundles(env.DB, controller.scheduledTime);
}
