import { env } from "cloudflare:test";
import { describe, expect, test } from "vitest";
import type { z } from "zod";

import contract from "../../contracts/mod-api-errors.json";
import ghostSummary from "../../contracts/v6/ghost-summary.response.json";
import { ROUTES } from "../../src/http/routes";
import worker from "../../src/index";
import bazaarDbPage from "../goldens/bazaardb-page.json";
import collectionPage from "../goldens/collection-page.json";
import ingestRunOnly from "../goldens/ingest-run-only.json";
import ingestDuplicate from "../goldens/ingest-screenshot-duplicate.json";

const ORIGIN = "https://mod-api-v6.bazaarplusplus.com";

interface OpenApiDocument {
  openapi: string;
  paths: Record<string, Record<string, { responses: Record<string, unknown> }>>;
  components: { schemas: Record<string, { properties?: Record<string, unknown> }> };
}

async function fetchDocument(): Promise<OpenApiDocument> {
  const response = await worker.fetch(new Request(`${ORIGIN}/openapi.json`), env);
  expect(response.status).toBe(200);
  expect(response.headers.get("content-type")).toBe("application/json; charset=utf-8");
  return response.json();
}

// The documented success schema for one route and status, straight from the route table.
function documentedSchema(path: string, method: string, status: number): z.ZodType {
  const route = ROUTES.find((candidate) => candidate.path === path && candidate.method === method);
  const responses = route?.docs.responses as
    | Record<string, { content?: { "application/json"?: { schema?: z.ZodType } } }>
    | undefined;
  const schema = responses?.[String(status)]?.content?.["application/json"]?.schema;
  if (schema === undefined) {
    throw new Error(`${method} ${path} documents no JSON ${status} response`);
  }
  return schema;
}

describe("the OpenAPI document", () => {
  // The rendered document is reviewed as a diff like every other golden.
  test("matches its golden", async () => {
    const document = await fetchDocument();
    await expect(`${JSON.stringify(document, null, 2)}\n`).toMatchFileSnapshot(
      "../goldens/openapi.json",
    );
  });

  test("lists exactly the route table", async () => {
    const document = await fetchDocument();
    expect(document.openapi).toBe("3.1.0");
    const documented = Object.entries(document.paths).flatMap(([path, item]) =>
      Object.keys(item).map((method) => `${method.toUpperCase()} ${path}`),
    );
    expect(documented.sort()).toEqual(ROUTES.map((r) => `${r.method} ${r.path}`).sort());
  });

  test("declares every server-owned error code", async () => {
    const document = await fetchDocument();
    const error = document.components.schemas.ApiErrorResponse.properties?.error as {
      properties: { code: { enum: string[] } };
    };
    expect(error.properties.code.enum).toEqual(contract.codes.map(({ code }) => code));
  });

  test("protects exactly the token routes", async () => {
    const document = await fetchDocument();
    const secured = Object.entries(document.paths).flatMap(([path, item]) =>
      Object.entries(item)
        .filter(([, operation]) => "security" in operation)
        .map(([method]) => `${method.toUpperCase()} ${path}`),
    );
    expect(secured.sort()).toEqual(["GET /bazaardb/bundles", "GET /bundles"]);
  });
});

// Goldens are real handler output, so a schema that rejects one is a stale document.
test.each([
  ["/bundles", "GET", collectionPage],
  ["/bazaardb/bundles", "GET", bazaarDbPage],
  ["/bundles", "POST", ingestRunOnly],
  ["/bundles", "POST", ingestDuplicate],
  ["/ghost-battles", "GET", { status: 200, body: ghostSummary }],
])("%s %s responses match the documented schema", (path, method, golden) => {
  const result = documentedSchema(path, method, golden.status).safeParse(golden.body);
  expect(result.error?.issues).toBeUndefined();
});

test("GET /health matches the documented schema", async () => {
  const response = await worker.fetch(new Request(`${ORIGIN}/health`), {} as Cloudflare.Env);
  const result = documentedSchema("/health", "GET", 200).safeParse(await response.json());
  expect(result.error?.issues).toBeUndefined();
});

test("GET /docs serves the Scalar reference for the document", async () => {
  const response = await worker.fetch(new Request(`${ORIGIN}/docs`), {} as Cloudflare.Env);
  expect(response.status).toBe(200);
  expect(response.headers.get("content-type")).toBe("text/html; charset=utf-8");
  expect(response.headers.get("x-request-id")).toBeTruthy();
  const html = await response.text();
  expect(html).toMatch(/cdn\.jsdelivr\.net\/npm\/@scalar\/api-reference@\d+\.\d+\.\d+"/);
  expect(html).toContain('url: "/openapi.json"');
});
