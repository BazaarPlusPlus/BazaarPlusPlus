import { env } from "cloudflare:test";
import { describe, expect, test } from "vitest";

import {
  type CommitObserver,
  commitBundle,
  inspectExistingBundle,
} from "../../src/modules/bundle-commit";
import { bundleData } from "../fixtures/bundle";

const TIMES = { availableAtMs: 1_785_628_900_000, storedAtMs: 1_785_628_899_000 };

describe("bundle commit", () => {
  test("returns no existing outcome when both Bundle and Run identities are absent", async () => {
    const data = await bundleData({
      bundleId: "01J00000000000000000000410",
      runId: "commit-absent-run",
      battles: [],
    });

    await expect(inspectExistingBundle(env.DB, data.descriptor, data.digest)).resolves.toBeNull();
  });

  test("commits a self-opponent Ghost projection", async () => {
    const data = await bundleData({
      bundleId: "01J00000000000000000000401",
      runId: "commit-self-run",
      uploaderAccountId: "commit-self-uploader",
    });

    await expect(commitBundle(env.DB, data.descriptor, data.digest, TIMES)).resolves.toEqual({
      kind: "committed",
      projection: { eligible: 1, inserted: 1 },
    });
    expect(
      await env.DB.prepare(
        `SELECT bundle_id, opponent_account_id FROM ghost_battle_summaries
         WHERE uploader_account_id = 'commit-self-uploader'`,
      ).first(),
    ).toEqual({
      bundle_id: data.descriptor.bundleId,
      opponent_account_id: "commit-self-uploader",
    });
  });

  test("filters an opponent absent from bundle_uploaders", async () => {
    const data = await bundleData({
      bundleId: "01J00000000000000000000402",
      runId: "commit-unknown-run",
      uploaderAccountId: "commit-unknown-uploader",
      opponentAccountId: "commit-never-uploaded-opponent",
    });

    await expect(commitBundle(env.DB, data.descriptor, data.digest, TIMES)).resolves.toEqual({
      kind: "committed",
      projection: { eligible: 0, inserted: 0 },
    });
    expect(
      await env.DB.prepare(`SELECT battle_id FROM ghost_battle_summaries WHERE bundle_id = ?1`)
        .bind(data.descriptor.bundleId)
        .first(),
    ).toBeNull();
  });

  test("commits a projection for a previously recorded uploader", async () => {
    const opponent = "commit-known-opponent";
    await env.DB.prepare(
      `INSERT INTO bundle_uploaders (player_account_id, first_bundle_at_ms) VALUES (?1, ?2)`,
    )
      .bind(opponent, TIMES.availableAtMs - 1)
      .run();
    const data = await bundleData({
      bundleId: "01J00000000000000000000403",
      runId: "commit-known-run",
      uploaderAccountId: "commit-known-uploader",
      opponentAccountId: opponent,
    });

    await expect(commitBundle(env.DB, data.descriptor, data.digest, TIMES)).resolves.toEqual({
      kind: "committed",
      projection: { eligible: 1, inserted: 1 },
    });
    expect(
      await env.DB.prepare(
        `SELECT opponent_account_id FROM ghost_battle_summaries WHERE bundle_id = ?1`,
      )
        .bind(data.descriptor.bundleId)
        .first(),
    ).toEqual({ opponent_account_id: opponent });
  });

  test("preserves the first cross-Bundle projection and reports the dropped duplicate", async () => {
    const uploader = "commit-duplicate-uploader";
    const first = await bundleData({
      bundleId: "01J00000000000000000000404",
      runId: "commit-duplicate-run-1",
      uploaderAccountId: uploader,
    });
    const second = await bundleData({
      bundleId: "01J00000000000000000000405",
      runId: "commit-duplicate-run-2",
      uploaderAccountId: uploader,
    });
    await commitBundle(env.DB, first.descriptor, first.digest, TIMES);
    const events: Array<{ bundle_id: string; dropped: number }> = [];
    const observer: CommitObserver = {
      projectionDuplicate(fields) {
        events.push(fields);
      },
    };

    await expect(
      commitBundle(env.DB, second.descriptor, second.digest, TIMES, observer),
    ).resolves.toEqual({ kind: "committed", projection: { eligible: 1, inserted: 0 } });
    expect(events).toEqual([{ bundle_id: second.descriptor.bundleId, dropped: 1 }]);
    expect(
      await env.DB.prepare(
        `SELECT bundle_id FROM ghost_battle_summaries
         WHERE uploader_account_id = ?1 AND battle_id = 'battle-001'`,
      )
        .bind(uploader)
        .first(),
    ).toEqual({ bundle_id: first.descriptor.bundleId });
  });

  test("returns an existing delivery receipt for a duplicate Screenshot Bundle", async () => {
    const data = await bundleData({
      bundleId: "01J00000000000000000000406",
      runId: "commit-screenshot-run",
      uploaderAccountId: "commit-screenshot-uploader",
      screenshotBytes: new Uint8Array([0xff, 0xd8, 1, 2, 0xff, 0xd9]),
      battles: [],
    });
    await commitBundle(env.DB, data.descriptor, data.digest, TIMES);

    await expect(commitBundle(env.DB, data.descriptor, data.digest, TIMES)).resolves.toEqual({
      kind: "duplicate",
      receipt: {
        bundle_id: data.descriptor.bundleId,
        run_id: data.descriptor.runId,
        outcome: "duplicate",
        bazaardb_delivery: "existing",
      },
    });
  });

  test("uses one decision table for Bundle ID and Run conflicts", async () => {
    const seed = await bundleData({
      bundleId: "01J00000000000000000000407",
      runId: "commit-conflict-run",
      uploaderAccountId: "commit-conflict-uploader",
      battles: [],
    });
    await commitBundle(env.DB, seed.descriptor, seed.digest, TIMES);
    const changedBundle = await bundleData({
      bundleId: seed.descriptor.bundleId,
      runId: "commit-conflict-other-run",
      uploaderAccountId: "commit-conflict-uploader",
      runBytes: new Uint8Array([1, 2, 3]),
      battles: [],
    });
    const reusedRun = await bundleData({
      bundleId: "01J00000000000000000000408",
      runId: seed.descriptor.runId,
      uploaderAccountId: "commit-conflict-other-uploader",
      battles: [],
    });

    await expect(
      inspectExistingBundle(env.DB, changedBundle.descriptor, changedBundle.digest),
    ).resolves.toEqual({ kind: "conflict", reason: "bundle_id_conflict" });
    await expect(
      inspectExistingBundle(env.DB, reusedRun.descriptor, reusedRun.digest),
    ).resolves.toEqual({ kind: "conflict", reason: "run_already_bundled" });
  });

  test("records the uploader as the final successful commit effect", async () => {
    const uploader = "commit-final-uploader";
    const data = await bundleData({
      bundleId: "01J00000000000000000000409",
      runId: "commit-final-run",
      uploaderAccountId: uploader,
      battles: [],
    });
    await commitBundle(env.DB, data.descriptor, data.digest, TIMES);

    expect(
      await env.DB.prepare(
        `SELECT first_bundle_at_ms FROM bundle_uploaders WHERE player_account_id = ?1`,
      )
        .bind(uploader)
        .first(),
    ).toEqual({ first_bundle_at_ms: TIMES.availableAtMs });
  });
});
