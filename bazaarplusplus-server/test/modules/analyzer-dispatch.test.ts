import { createScheduledController, env } from "cloudflare:test";
import { afterEach, expect, test, vi } from "vitest";

import worker from "../../src/index";
import {
  ANALYZER_DISPATCH_CRON,
  ANALYZER_WORKFLOW_DISPATCH_URL,
  dispatchAnalyzer,
} from "../../src/modules/analyzer-dispatch";
import wranglerToml from "../../wrangler.toml?raw";

const NOW = 1_785_628_800_000;
const CREDENTIAL = "github_pat_test_credential";

afterEach(() => vi.restoreAllMocks());

function dispatchEnv(credential?: string): Cloudflare.Env {
  return { DB: env.DB, GITHUB_ANALYZER_DISPATCH_TOKEN: credential } as Cloudflare.Env;
}

test("wrangler.toml schedules the analyzer dispatch cron", () => {
  expect(wranglerToml).toContain(`"${ANALYZER_DISPATCH_CRON}"`);
});

test("the dispatch cron starts one publishing analyzer run on master and skips retention", async () => {
  const fetchMock = vi
    .spyOn(globalThis, "fetch")
    .mockResolvedValue(new Response(null, { status: 204 }));
  const log = vi.spyOn(console, "log").mockImplementation(() => undefined);
  const prepare = vi.spyOn(env.DB, "prepare");

  await worker.scheduled(
    createScheduledController({ scheduledTime: NOW, cron: ANALYZER_DISPATCH_CRON }),
    dispatchEnv(CREDENTIAL),
  );

  expect(fetchMock).toHaveBeenCalledTimes(1);
  const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
  expect(url).toBe(ANALYZER_WORKFLOW_DISPATCH_URL);
  expect(init.method).toBe("POST");
  const headers = new Headers(init.headers);
  expect(headers.get("Authorization")).toBe(`Bearer ${CREDENTIAL}`);
  expect(headers.get("User-Agent")).toBeTruthy();
  expect(JSON.parse(init.body as string)).toEqual({ ref: "master", inputs: { publish: "true" } });
  expect(prepare).not.toHaveBeenCalled();
  expect(log).toHaveBeenCalledWith(expect.stringContaining('"event":"analyzer.dispatch"'));
  expect(log.mock.calls.flat().join("\n")).not.toContain(CREDENTIAL);
});

test("the retention cron never dispatches the analyzer", async () => {
  const fetchMock = vi.spyOn(globalThis, "fetch");
  vi.spyOn(console, "log").mockImplementation(() => undefined);

  await worker.scheduled(
    createScheduledController({ scheduledTime: NOW, cron: "*/15 * * * *" }),
    dispatchEnv(CREDENTIAL),
  );

  expect(fetchMock).not.toHaveBeenCalled();
});

test("a missing credential skips the dispatch without calling GitHub", async () => {
  const fetchMock = vi.spyOn(globalThis, "fetch");
  const log = vi.spyOn(console, "log").mockImplementation(() => undefined);

  for (const credential of [undefined, ""]) {
    expect(await dispatchAnalyzer({ GITHUB_ANALYZER_DISPATCH_TOKEN: credential }, NOW)).toBe(
      "skipped",
    );
  }

  expect(fetchMock).not.toHaveBeenCalled();
  expect(log).toHaveBeenCalledWith(expect.stringContaining('"reason":"missing_credential"'));
});

test("GitHub errors and network failures are logged without failing the invocation", async () => {
  const error = vi.spyOn(console, "error").mockImplementation(() => undefined);
  const fetchMock = vi
    .spyOn(globalThis, "fetch")
    .mockResolvedValueOnce(new Response('{"message":"Bad credentials"}', { status: 401 }))
    .mockRejectedValueOnce(new TypeError("network down"));

  for (let attempt = 0; attempt < 2; attempt += 1) {
    await expect(
      worker.scheduled(
        createScheduledController({ scheduledTime: NOW, cron: ANALYZER_DISPATCH_CRON }),
        dispatchEnv(CREDENTIAL),
      ),
    ).resolves.toBeUndefined();
  }

  expect(fetchMock).toHaveBeenCalledTimes(2);
  const lines = error.mock.calls.flat().join("\n");
  expect(lines).toContain(
    '"event":"analyzer.dispatch.failed","scheduled_at_ms":1785628800000,"status":401',
  );
  expect(lines).toContain('"error":"TypeError"');
  expect(lines).not.toContain(CREDENTIAL);
});
