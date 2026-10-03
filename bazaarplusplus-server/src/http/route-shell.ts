import type { Env } from "../env";
import { HttpError } from "../errors";
import { logError } from "../observability";
import { authenticateServiceToken, type ServiceScope } from "./auth";
import { jsonError, jsonResponse } from "./json";
import { V5_ROUTES } from "./routes";

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
  readonly cors?: boolean;
  readonly handler: RouteHandler;
}

interface IndexedPath {
  readonly cors: boolean;
  readonly methods: string[];
  readonly byMethod: Map<string, RouteDefinition>;
}

function indexRoutes(routes: readonly RouteDefinition[]): Map<string, IndexedPath> {
  const paths = new Map<string, IndexedPath>();
  for (const route of routes) {
    let indexed = paths.get(route.path);
    if (indexed === undefined) {
      indexed = { cors: route.cors ?? false, methods: [], byMethod: new Map() };
      paths.set(route.path, indexed);
    }
    indexed.methods.push(route.method);
    indexed.byMethod.set(route.method, route);
  }
  return paths;
}

function optionsResponse(route: IndexedPath): Response {
  const allow = route.methods.join(", ");
  const headers = new Headers({
    Allow: allow,
    "Access-Control-Allow-Methods": allow,
    "Access-Control-Allow-Headers": "Authorization, Content-Type, Content-Digest",
    "Access-Control-Max-Age": "86400",
  });
  if (route.cors) {
    headers.set("Access-Control-Allow-Origin", "*");
  }
  return new Response(null, { status: 204, headers });
}

function authDenial(
  outcome: ReturnType<typeof authenticateServiceToken>,
  requestId: string,
): Response {
  if (outcome === "insufficient_scope") {
    return jsonError(
      {
        code: "insufficient_scope",
        message: "The service token does not grant access to this route",
        retryable: false,
      },
      403,
      requestId,
    );
  }
  if (outcome === "invalid_configuration") {
    return jsonError(
      {
        code: "internal_error",
        message: "Service token configuration is invalid",
        retryable: true,
      },
      500,
      requestId,
    );
  }
  return jsonError(
    {
      code: "unauthorized",
      message: "A valid service token is required",
      retryable: false,
    },
    401,
    requestId,
  );
}

function corsHeaders(enabled: boolean, initial?: HeadersInit): Headers {
  const headers = new Headers(initial);
  if (enabled) {
    headers.set("Access-Control-Allow-Origin", "*");
  }
  return headers;
}

const paths = indexRoutes(V5_ROUTES);

export async function fetch(request: Request, env: Env): Promise<Response> {
  const requestId = request.headers.get("CF-Ray") ?? crypto.randomUUID();
  const path = new URL(request.url).pathname;
  const indexed = paths.get(path);
  if (indexed === undefined) {
    return jsonError(
      { code: "not_found", message: "Route not found", retryable: false },
      404,
      requestId,
    );
  }
  if (request.method === "OPTIONS") {
    return optionsResponse(indexed);
  }

  const route = indexed.byMethod.get(request.method);
  if (route === undefined) {
    return jsonError(
      { code: "method_not_allowed", message: "Method not allowed", retryable: false },
      405,
      requestId,
      { Allow: indexed.methods.join(", ") },
    );
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
      return authDenial(outcome, requestId);
    }
  }

  try {
    const result = await route.handler({ request, env, requestId });
    return jsonResponse(result.body, {
      status: result.status,
      requestId,
      headers: corsHeaders(indexed.cors),
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
      return jsonError(
        {
          code: error.code,
          message: error.message,
          retryable: error.retryable,
          details: error.details,
        },
        error.status,
        requestId,
        corsHeaders(indexed.cors, error.headers),
      );
    }
    logError("worker.internal_error", {
      request_id: requestId,
      route: path,
      error_name: error instanceof Error ? error.name : "unknown",
      reason: "unclassified_exception",
    });
    return jsonError(
      {
        code: "internal_error",
        message: "An internal error occurred",
        retryable: true,
      },
      500,
      requestId,
      corsHeaders(indexed.cors),
    );
  }
}
