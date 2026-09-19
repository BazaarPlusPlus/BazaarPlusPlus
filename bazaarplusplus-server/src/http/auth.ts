import type { Env } from "../env";

export type ServiceScope = "bundle_sync" | "bazaardb_delivery";

const TOKEN_BYTES = 43;

function constantTimeTokenEquals(candidate: string, configured: string): boolean {
  const candidateBytes = new TextEncoder().encode(candidate);
  const configuredBytes = new TextEncoder().encode(configured);
  let difference = candidateBytes.length ^ configuredBytes.length;

  for (let index = 0; index < TOKEN_BYTES; index += 1) {
    difference |= (candidateBytes[index] ?? 0) ^ (configuredBytes[index] ?? 0);
  }

  return difference === 0;
}

function configuredTokensAreValid(env: Env): boolean {
  const sync = env.BUNDLE_SYNC_TOKEN;
  const delivery = env.BAZAARDB_DELIVERY_TOKEN;
  return (
    typeof sync === "string" &&
    typeof delivery === "string" &&
    sync.length === TOKEN_BYTES &&
    delivery.length === TOKEN_BYTES &&
    /^[A-Za-z0-9_-]{43}$/.test(sync) &&
    /^[A-Za-z0-9_-]{43}$/.test(delivery) &&
    !constantTimeTokenEquals(sync, delivery)
  );
}

export function authenticateServiceToken(
  request: Request,
  env: Env,
  requiredScope: ServiceScope,
): "authorized" | "unauthorized" | "insufficient_scope" | "invalid_configuration" {
  if (!configuredTokensAreValid(env)) {
    return "invalid_configuration";
  }

  const header = request.headers.get("Authorization");
  const candidate = header?.startsWith("Bearer ") ? header.slice(7) : "";
  const syncMatches = constantTimeTokenEquals(candidate, env.BUNDLE_SYNC_TOKEN);
  const deliveryMatches = constantTimeTokenEquals(candidate, env.BAZAARDB_DELIVERY_TOKEN);

  if (requiredScope === "bundle_sync" && syncMatches) {
    return "authorized";
  }
  if (requiredScope === "bazaardb_delivery" && deliveryMatches) {
    return "authorized";
  }
  if (syncMatches || deliveryMatches) {
    return "insufficient_scope";
  }
  return "unauthorized";
}
