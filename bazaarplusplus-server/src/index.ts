import type { Env } from "./env";
import { fetch } from "./http/route-shell";
import { scheduled } from "./modules/d1-retention";

export default { fetch, scheduled } satisfies ExportedHandler<Env>;
