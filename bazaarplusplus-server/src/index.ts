import type { Env } from "./env";
import { fetch } from "./http/route-shell";
import { scheduled } from "./scheduled";

export default { fetch, scheduled } satisfies ExportedHandler<Env>;
