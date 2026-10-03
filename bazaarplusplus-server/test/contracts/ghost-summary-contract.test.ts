import { env } from "cloudflare:test";
import { afterEach, expect, test, vi } from "vitest";
import responseContract from "../../contracts/v5/ghost-summary.response.json";
import worker from "../../src/index";
import { type BundleFixtureOptions, makeBundleFixture, uploadRequest } from "../fixtures/bundle";

afterEach(() => vi.restoreAllMocks());

type Battle = Record<string, unknown> & {
  player: Record<string, unknown>;
  opponent: Record<string, unknown>;
};

// Uploads the fixture's default Battle after `edit`, then discovers it for `opponentAccountId`.
async function uploadAndDiscover(
  options: BundleFixtureOptions,
  edit: (battle: Battle) => void,
  opponentAccountId: string,
): Promise<unknown> {
  const base = await makeBundleFixture(options);
  const battle = structuredClone(
    (base.manifest.run as { projection: { battles: Battle[] } }).projection.battles[0],
  );
  edit(battle);
  const fixture = await makeBundleFixture({ ...options, battles: [battle] });
  const uploaded = await worker.fetch(uploadRequest(fixture.body, fixture.headers), env);
  expect(uploaded.status).toBe(201);
  const response = await worker.fetch(
    new Request(`https://worker.test/ghost-battles?player_account_id=${opponentAccountId}`),
    env,
  );
  expect(response.status).toBe(200);
  return response.json();
}

// The mod consumes this same contract in bazaarplusplus-mod/tests/ModApi.Tests/GhostSummaryContractTests.cs.
test("the discovery response exactly matches the Mod-compatible nested summary contract", async () => {
  const row = responseContract.battles[0];
  vi.spyOn(Date, "now").mockReturnValue(row.recorded_at_ms + 1000);
  await env.DB.prepare("INSERT INTO bundle_uploaders VALUES (?1, 1)")
    .bind(row.opponent.account_id)
    .run();
  const body = await uploadAndDiscover(
    {
      bundleId: row.bundle_id,
      runId: "ghost-contract-run",
      uploaderAccountId: row.player.account_id,
      opponentAccountId: row.opponent.account_id,
      createdAtMs: row.recorded_at_ms + 1000,
    },
    (battle) => {
      battle.result = "loss";
      battle.winner_combatant_id = "Opponent";
    },
    row.opponent.account_id,
  );
  expect(body).toEqual(responseContract);
});

test("nullable summary values and a zero challenger rating survive ordinary-column storage", async () => {
  const body = await uploadAndDiscover(
    {
      bundleId: "01J00000000000000000000802",
      runId: "ghost-nullable-run",
      uploaderAccountId: "nullable-uploader",
      createdAtMs: Date.now(),
    },
    (battle) => {
      battle.winner_combatant_id = null;
      battle.player.hero_name = null;
      battle.opponent.hero_name = null;
      battle.player.rank = null;
      battle.player.rating = 0;
    },
    "nullable-uploader",
  );
  expect(body).toMatchObject({
    battles: [
      {
        winner_combatant_id: null,
        player: { hero_name: null, rank: null, rating: 0 },
        opponent: { hero_name: null },
      },
    ],
  });
});
