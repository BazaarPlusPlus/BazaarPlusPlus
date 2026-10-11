import { collectBazaarDbBundles, collectBundles } from "../modules/bundle-collection";
import { ingestBundle } from "../modules/bundle-ingest";
import { discoverGhostBattles } from "../modules/ghost-battle-discovery";
import type { RouteDefinition } from "./route-shell";

export const V5_ROUTES = [
  {
    path: "/health",
    method: "GET",
    cors: true,
    handler: async () => ({
      status: 200,
      body: { status: "ok", server_time_ms: Date.now() },
    }),
  },
  {
    path: "/bundles",
    method: "GET",
    auth: "bundle_sync",
    handler: async (context) => ({
      status: 200,
      body: await collectBundles(context.request, context.env, context.requestId),
    }),
  },
  {
    path: "/bundles",
    method: "POST",
    handler: async (context) => {
      const result = await ingestBundle(context.request, context.env, context.requestId);
      return { status: result.status, body: result.receipt };
    },
  },
  {
    path: "/ghost-battles",
    method: "GET",
    cors: true,
    handler: async (context) => ({
      status: 200,
      body: await discoverGhostBattles(context.request, context.env, context.requestId),
    }),
  },
  {
    path: "/bazaardb/bundles",
    method: "GET",
    auth: "bazaardb_delivery",
    handler: async (context) => ({
      status: 200,
      body: await collectBazaarDbBundles(context.request, context.env, context.requestId),
    }),
  },
] as const satisfies readonly RouteDefinition[];
