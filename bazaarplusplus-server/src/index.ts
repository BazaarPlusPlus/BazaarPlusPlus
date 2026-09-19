import type { Env } from "./env";
import { createFetchHandler } from "./http/route-shell";
import { V5_ROUTES } from "./http/routes";
import { scheduled } from "./modules/d1-retention";

const fetch = createFetchHandler(V5_ROUTES);

export default { fetch, scheduled } satisfies ExportedHandler<Env>;
