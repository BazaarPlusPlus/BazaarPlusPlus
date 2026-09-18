import { env } from "cloudflare:test";
import { expect, test } from "vitest";
import responseContract from "../../contracts/v5/ghost-summary.response.json";
import { commitBundle } from "../../src/modules/bundle-commit";
import { discoverGhostBattles } from "../../src/modules/ghost-battle-discovery";
import { bundleData } from "../fixtures/bundle";
import { createTestDeps } from "../fixtures/deps";

// The real ModApi DLL consumes this same contract in scripts/ghost-projection/mod-compat.
test("the discovery response exactly matches the Mod-compatible nested summary contract", async () => {
  const row = responseContract.battles[0];
  const fixture = await bundleData({
    bundleId: row.bundle_id,
    runId: "ghost-contract-run",
    uploaderAccountId: row.player.account_id,
    opponentAccountId: row.opponent.account_id,
    createdAtMs: row.recorded_at_ms + 1000,
  });
  await env.DB.prepare("INSERT INTO bundle_uploaders VALUES (?1, 1)")
    .bind(row.opponent.account_id)
    .run();
  fixture.descriptor.battles[0].result = "loss";
  fixture.descriptor.battles[0].winner_combatant_id = "Opponent";
  await commitBundle(env.DB, fixture.descriptor, fixture.digest, {
    storedAtMs: row.recorded_at_ms,
    availableAtMs: row.recorded_at_ms,
  });
  const result = await discoverGhostBattles(
    new Request(`https://worker.test/ghost-battles?player_account_id=${row.opponent.account_id}`),
    env,
    "contract",
    createTestDeps({
      now: () => row.recorded_at_ms + 1000,
      signer: {
        async sign() {
          return { url: row.download_url, expiresAtMs: row.download_expires_at_ms };
        },
      },
    }),
  );
  expect(result).toEqual(responseContract);
  // Ingest still validates and retains the complete manifest descriptor.
  expect(fixture.descriptor.battles[0]).toMatchObject({
    combat_kind: "pvp",
    loser_combatant_id: "combatant-opponent",
    player: { level: 10, prestige: 2, victories: 9 },
    opponent: { display_name: "Opponent", rank: "Gold", rating: 1200 },
  });
});

test("nullable summary values and a zero challenger rating survive ordinary-column storage", async () => {
  const fixture = await bundleData({
    bundleId: "01J00000000000000000000802",
    runId: "ghost-nullable-run",
    uploaderAccountId: "nullable-uploader",
  });
  const projection = fixture.descriptor.battles[0];
  projection.winner_combatant_id = null;
  projection.player.hero_name = null;
  projection.opponent.hero_name = null;
  projection.player.rank = null;
  projection.player.rating = 0;
  await commitBundle(env.DB, fixture.descriptor, fixture.digest, {
    storedAtMs: 1,
    availableAtMs: 1,
  });
  const result = await discoverGhostBattles(
    new Request("https://worker.test/ghost-battles?player_account_id=nullable-uploader"),
    env,
    "nullable",
    createTestDeps({ now: () => projection.recorded_at_ms + 1 }),
  );
  expect(result.battles).toMatchObject([
    {
      winner_combatant_id: null,
      player: { hero_name: null, rank: null, rating: 0 },
      opponent: { hero_name: null },
    },
  ]);
});
