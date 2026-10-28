import {
  createDocument,
  type ZodOpenApiOperationObject,
  type ZodOpenApiPathsObject,
  type ZodOpenApiResponseObject,
  type ZodOpenApiResponsesObject,
} from "zod-openapi";
import contract from "../../contracts/mod-api-errors.json";
import type { ApiErrorCode } from "../errors";
import type { ServiceScope } from "./auth";
import type { RouteDefinition } from "./route-shell";
import { ApiErrorResponse } from "./schemas";

// Pinned so the docs page cannot change under a deploy; bump deliberately.
const SCALAR_SCRIPT = "https://cdn.jsdelivr.net/npm/@scalar/api-reference@1.74.0";

export const PRODUCTION_ORIGIN = "https://mod-api-v6.bazaarplusplus.com";

// A route documents its success responses and the error codes it can return;
// the builder adds the shared error envelope, X-Request-Id, and security.
export interface RouteDocs extends Omit<ZodOpenApiOperationObject, "responses" | "security"> {
  readonly responses: ZodOpenApiResponsesObject;
  readonly errors: readonly ApiErrorCode[];
}

const SECURITY_SCHEMES: Record<ServiceScope, { name: string; description: string }> = {
  bundle_sync: {
    name: "BundleSyncToken",
    description: "`BUNDLE_SYNC_TOKEN`, a 43-character unpadded base64url service token",
  },
  bazaardb_delivery: {
    name: "BazaarDbDeliveryToken",
    description: "`BAZAARDB_DELIVERY_TOKEN`, a 43-character unpadded base64url service token",
  },
};

const REQUEST_ID_HEADER = {
  "X-Request-Id": { description: "CF-Ray, or a server UUID", schema: { type: "string" } },
} as const;

const ERRORS = new Map(contract.codes.map((row) => [row.code, row]));

function withRequestId(response: ZodOpenApiResponseObject): ZodOpenApiResponseObject {
  return { ...response, headers: { ...REQUEST_ID_HEADER, ...response.headers } };
}

function errorResponses(codes: readonly ApiErrorCode[]): ZodOpenApiResponsesObject {
  const byStatus = new Map<number, ApiErrorCode[]>();
  for (const code of codes) {
    const { status } = ERRORS.get(code) as { status: number };
    byStatus.set(status, [...(byStatus.get(status) ?? []), code]);
  }
  const responses: ZodOpenApiResponsesObject = {};
  for (const [status, statusCodes] of [...byStatus].sort(([a], [b]) => a - b)) {
    const lines = statusCodes.map((code) => {
      const row = ERRORS.get(code) as { meaning: string; retryable: boolean };
      return `- \`${code}\`${row.retryable ? " (retryable)" : ""}: ${row.meaning}`;
    });
    responses[String(status) as `${1 | 2 | 3 | 4 | 5}${string}`] = withRequestId({
      description: lines.join("\n"),
      headers: statusCodes.includes("rate_limited")
        ? { "Retry-After": { description: "Seconds to wait", schema: { const: "60" } } }
        : undefined,
      content: { "application/json": { schema: ApiErrorResponse } },
    });
  }
  return responses;
}

export function buildOpenApiDocument(
  routes: readonly RouteDefinition[],
): ReturnType<typeof createDocument> {
  const paths: ZodOpenApiPathsObject = {};
  for (const { path, method, auth, docs } of routes) {
    const { errors, responses, ...operation } = docs;
    const success = Object.fromEntries(
      Object.entries(responses).map(([status, response]) => [
        status,
        withRequestId(response as ZodOpenApiResponseObject),
      ]),
    );
    paths[path] = {
      ...paths[path],
      [method.toLowerCase()]: {
        ...operation,
        security: auth === undefined ? undefined : [{ [SECURITY_SCHEMES[auth].name]: [] }],
        // The route shell turns any unclassified exception into internal_error.
        responses: { ...success, ...errorResponses([...errors, "internal_error"]) },
      },
    };
  }

  return createDocument({
    openapi: "3.1.0",
    info: {
      title: "BazaarPlusPlus Mod API V6",
      version: "6",
      description:
        "The mod-facing backend for Bundle V6. Errors share one envelope; clients act on the " +
        "status, `code`, and `retryable`, never on `message`. A known path with another " +
        "method returns `405 method_not_allowed` with `Allow`, and an unknown path returns " +
        "`404 not_found`. No route sends CORS headers.",
    },
    servers: [{ url: PRODUCTION_ORIGIN }],
    tags: [
      { name: "Health", description: "Liveness" },
      { name: "Ingest", description: "Public Bundle upload" },
      { name: "Discovery", description: "Bundle and Ghost Battle discovery" },
      { name: "Documentation", description: "This document and its reference page" },
    ],
    components: {
      securitySchemes: Object.fromEntries(
        Object.values(SECURITY_SCHEMES).map(({ name, description }) => [
          name,
          { type: "http", scheme: "bearer", description },
        ]),
      ),
    },
    paths,
  });
}

export function scalarPage(specPath: string): string {
  return `<!doctype html>
<html lang="en">
  <head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>BazaarPlusPlus Mod API V6</title>
  </head>
  <body>
    <div id="app"></div>
    <script src="${SCALAR_SCRIPT}"></script>
    <script>
      Scalar.createApiReference("#app", { url: "${specPath}" });
    </script>
  </body>
</html>
`;
}
