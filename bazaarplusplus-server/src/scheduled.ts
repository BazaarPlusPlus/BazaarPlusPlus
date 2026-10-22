import type { Env } from "./env";
import { pruneExpiredBundles } from "./modules/d1-retention";

export async function scheduled(controller: ScheduledController, env: Env): Promise<void> {
  await pruneExpiredBundles(env.DB, controller.scheduledTime);
}
