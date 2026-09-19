import { setDefaultResultOrder } from "node:dns";
import { readFile } from "node:fs/promises";
import { homedir } from "node:os";
import { join } from "node:path";

export async function cloudflare() {
  setDefaultResultOrder("ipv4first");
  const config = await readFile(new URL("../../wrangler.toml", import.meta.url), "utf8");
  const field = (name) => new RegExp(`^${name} = "([^"]+)"`, "m").exec(config)?.[1];
  const account = field("R2_ACCOUNT_ID");
  const database = field("database_id");
  const worker = field("name");
  let token = process.env.CLOUDFLARE_API_TOKEN;
  if (!token) {
    const settings = await readFile(
      join(homedir(), "Library/Preferences/.wrangler/config/default.toml"),
      "utf8",
    );
    token = /^oauth_token\s*=\s*"([^"]+)"/m.exec(settings)?.[1];
  }
  if (!token || !account || !database || !worker)
    throw new Error("Cloudflare configuration or credentials unavailable");
  const request = async (path, body) => {
    const response = await fetch(
      `https://api.cloudflare.com/client/v4/accounts/${account}/${path}`,
      {
        method: body === undefined ? "GET" : "POST",
        headers: { Authorization: `Bearer ${token}`, "Content-Type": "application/json" },
        body: body === undefined ? undefined : JSON.stringify(body),
        signal: AbortSignal.timeout(45000),
      },
    );
    const payload = await response.json();
    if (
      response.status === 401 ||
      (response.status === 403 && payload.errors?.some((error) => error.code === 7403))
    ) {
      throw new Error(
        "Cloudflare authentication failed; refresh with npx wrangler whoami, then resume or verify token permissions",
      );
    }
    if (!response.ok || !payload.success) {
      throw new Error(
        `Cloudflare request failed (${response.status}; codes: ${(payload.errors ?? []).map((x) => x.code).join(",")})`,
      );
    }
    return payload.result;
  };
  return {
    database,
    worker,
    metadata: () => request(`d1/database/${database}`),
    deployments: () => request(`workers/scripts/${worker}/deployments`),
    query: async (sql, params = []) => {
      const result = await request(`d1/database/${database}/query`, { sql, params });
      if (result.some((r) => !r.success)) throw new Error("D1 query failed");
      return result;
    },
  };
}
