import { claimDeliveries, settleDeliveries } from "../modules/bazaardb-delivery";
import { collectBundles } from "../modules/bundle-collection";
import { ingestBundle } from "../modules/bundle-ingest";
import { discoverGhostBattles } from "../modules/ghost-battle-discovery";
import type { RouteDefinition } from "./route-shell";

export const V5_ROUTES = [
  {
    path: "/health",
    method: "GET",
    cors: true,
    handler: async (context) => ({
      status: 200,
      body: { status: "ok", server_time_ms: context.deps.now() },
    }),
  },
  {
    path: "/bundles",
    method: "GET",
    auth: "bundle_sync",
    handler: async (context) => ({
      status: 200,
      body: await collectBundles(context.request, context.env, context.requestId, context.deps),
    }),
  },
  {
    path: "/bundles",
    method: "POST",
    handler: async (context) => {
      const result = await ingestBundle(
        context.request,
        context.env,
        context.requestId,
        context.deps,
      );
      return { status: result.status, body: result.receipt };
    },
  },
  {
    path: "/ghost-battles",
    method: "GET",
    cors: true,
    handler: async (context) => ({
      status: 200,
      body: await discoverGhostBattles(
        context.request,
        context.env,
        context.requestId,
        context.deps,
      ),
    }),
  },
  {
    path: "/bazaardb/deliveries/claim",
    method: "POST",
    auth: "bazaardb_delivery",
    handler: async (context) => ({
      status: 200,
      body: await claimDeliveries(context.request, context.env, context.requestId, context.deps),
    }),
  },
  {
    path: "/bazaardb/deliveries/settle",
    method: "POST",
    auth: "bazaardb_delivery",
    handler: async (context) => ({
      status: 200,
      body: await settleDeliveries(context.request, context.env, context.requestId, context.deps),
    }),
  },
] as const satisfies readonly RouteDefinition[];
