import { expect, test } from "vitest";

import worker from "../../src/index";

test("GET /health reports V5 liveness without dependency access", async () => {
  const response = await worker.fetch(
    new Request("https://mod-api-v5.bazaarplusplus.com/health"),
    {} as Cloudflare.Env,
  );

  expect(response.status).toBe(200);
  expect(response.headers.get("content-type")).toBe("application/json; charset=utf-8");
  expect(response.headers.get("cache-control")).toBe("no-store");
  expect(response.headers.get("x-request-id")).toBeTruthy();

  const body = (await response.json()) as {
    status: string;
    server_time_ms: number;
  };
  expect(body).toMatchObject({ status: "ok" });
  expect(Number.isSafeInteger(body.server_time_ms)).toBe(true);
});
