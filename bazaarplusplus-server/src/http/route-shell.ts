import type { Env } from "../env";
import { apiError, HttpError } from "../errors";
import { logError } from "../observability";
import { authenticateServiceToken, type ServiceScope } from "./auth";
import { jsonError, jsonResponse } from "./json";
import { ROUTES } from "./routes";

export interface HandlerContext {
  readonly request: Request;
  readonly env: Env;
  readonly requestId: string;
}

export interface HandlerResult {
  readonly status: number;
  readonly body: unknown;
}

export type RouteHandler = (context: HandlerContext) => Promise<HandlerResult>;

export interface RouteDefinition {
  readonly path: string;
  readonly method: "GET" | "POST";
  readonly auth?: ServiceScope;
  readonly handler: RouteHandler;
}

interface IndexedPath {
  readonly methods: string[];
  readonly byMethod: Map<string, RouteDefinition>;
}

function indexRoutes(routes: readonly RouteDefinition[]): Map<string, IndexedPath> {
  const paths = new Map<string, IndexedPath>();
  for (const route of routes) {
    let indexed = paths.get(route.path);
    if (indexed === undefined) {
      indexed = { methods: [], byMethod: new Map() };
      paths.set(route.path, indexed);
    }
    indexed.methods.push(route.method);
    indexed.byMethod.set(route.method, route);
  }
  return paths;
}

function errorResponse(error: HttpError, requestId: string, headers?: HeadersInit): Response {
  return jsonError(
    {
      code: error.code,
      message: error.message,
      retryable: error.retryable,
      details: error.details,
    },
    error.status,
    requestId,
    headers,
  );
}

function authDenial(outcome: ReturnType<typeof authenticateServiceToken>): HttpError {
  if (outcome === "insufficient_scope") {
    return apiError("insufficient_scope", "The service token does not grant access to this route");
  }
  if (outcome === "invalid_configuration") {
    return apiError("internal_error", "Service token configuration is invalid");
  }
  return apiError("unauthorized", "A valid service token is required");
}

const paths = indexRoutes(ROUTES);

export async function fetch(request: Request, env: Env): Promise<Response> {
  const requestId = request.headers.get("CF-Ray") ?? crypto.randomUUID();
  const path = new URL(request.url).pathname;
  const indexed = paths.get(path);
  if (indexed === undefined) {
    return errorResponse(apiError("not_found", "Route not found"), requestId);
  }
  const route = indexed.byMethod.get(request.method);
  if (route === undefined) {
    return errorResponse(apiError("method_not_allowed", "Method not allowed"), requestId, {
      Allow: indexed.methods.join(", "),
    });
  }

  if (route.auth !== undefined) {
    const outcome = authenticateServiceToken(request, env, route.auth);
    if (outcome !== "authorized") {
      if (outcome === "invalid_configuration") {
        logError("worker.http_error", {
          request_id: requestId,
          route: path,
          status: 500,
          code: "invalid_configuration",
        });
      }
      return errorResponse(authDenial(outcome), requestId);
    }
  }

  try {
    const result = await route.handler({ request, env, requestId });
    return jsonResponse(result.body, {
      status: result.status,
      requestId,
    });
  } catch (error) {
    if (error instanceof HttpError) {
      if (error.status >= 500) {
        logError("worker.http_error", {
          request_id: requestId,
          route: path,
          status: error.status,
          code: error.code,
        });
      }
      return errorResponse(error, requestId, error.headers);
    }
    logError("worker.internal_error", {
      request_id: requestId,
      route: path,
      error_name: error instanceof Error ? error.name : "unknown",
      reason: "unclassified_exception",
    });
    return errorResponse(apiError("internal_error", "An internal error occurred"), requestId);
  }
}
