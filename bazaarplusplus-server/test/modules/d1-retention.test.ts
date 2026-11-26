import { createScheduledController, env } from 'cloudflare:test';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';

import worker from '../../src/index';
import {
  D1_RETENTION_BATCH_SIZE,
  D1_RETENTION_MAX_BATCHES,
  D1_RETENTION_MS
} from '../../src/limits';
import { pruneExpiredBundles } from '../../src/modules/d1-retention';
import { seedBundleBacklog } from '../fixtures/backlog';
import { recordD1 } from '../fixtures/d1';

const NOW = 1_785_628_800_000;
const CUTOFF = NOW - D1_RETENTION_MS;
const id = (index: number) => `01J9${String(index).padStart(22, '0')}`;

beforeEach(async () => {
  await env.DB.prepare('DELETE FROM bundles').run();
});
afterEach(() => vi.restoreAllMocks());

test('scheduled retention uses stored time, cascades children and preserves uploaders', async () => {
  await seedBundleBacklog(env.DB, 3, CUTOFF - 1);
  await env.DB.batch([
    env.DB.prepare(
      'UPDATE bundles SET stored_at_ms = ?1 WHERE bundle_id = ?2'
    ).bind(CUTOFF, id(1)),
    env.DB.prepare(
      'UPDATE bundles SET stored_at_ms = ?1 WHERE bundle_id = ?2'
    ).bind(CUTOFF + 1, id(2)),
    env.DB.prepare(
      'UPDATE bundles SET client_created_at_ms = ?1 WHERE bundle_id = ?2'
    ).bind(NOW, id(0)),
    env.DB.prepare(`INSERT INTO bundle_uploaders (player_account_id)
      VALUES ('retained-uploader')`),
    env.DB.prepare(`INSERT INTO ghost_battle_summaries (
      uploader_account_id, battle_id, bundle_id, opponent_account_id, recorded_at_ms, day, hour, result, winner_combatant_id, player_display_name
    ) SELECT 'retained-uploader', run_id, bundle_id, 'retained-uploader', stored_at_ms, 1, 1, 'win', 'Player', 'Uploader'
      FROM bundles`)
  ]);

  const log = vi.spyOn(console, 'log').mockImplementation(() => undefined);
  await worker.scheduled(createScheduledController({ scheduledTime: NOW }), {
    DB: env.DB
  } as Cloudflare.Env);
  for (const table of ['bundles', 'ghost_battle_summaries']) {
    const rows = await env.DB.prepare(
      `SELECT bundle_id FROM ${table} ORDER BY bundle_id`
    ).all();
    expect(rows.results).toEqual([{ bundle_id: id(1) }, { bundle_id: id(2) }]);
  }
  expect(
    await env.DB.prepare(
      'SELECT player_account_id FROM bundle_uploaders WHERE player_account_id = ?1'
    )
      .bind('retained-uploader')
      .first()
  ).toEqual({ player_account_id: 'retained-uploader' });
  expect(log).toHaveBeenCalledWith(
    expect.stringContaining('"deleted_bundles":1')
  );
  expect(await pruneExpiredBundles(env.DB, NOW)).toEqual({
    deleted: 0,
    hasMore: false
  });
  expect(await pruneExpiredBundles(env.DB, NOW + 1)).toEqual({
    deleted: 1,
    hasMore: false
  });
});

test('retention has bounded indexed batches and resumes the remaining backlog', async () => {
  const budget = D1_RETENTION_BATCH_SIZE * D1_RETENTION_MAX_BATCHES;
  await seedBundleBacklog(env.DB, budget + 3, CUTOFF - 1);
  const queries = recordD1(env.DB);
  expect(await pruneExpiredBundles(env.DB, NOW)).toEqual({
    deleted: budget,
    hasMore: true
  });
  expect(queries).toHaveLength(D1_RETENTION_MAX_BATCHES);
  for (const query of queries) {
    expect(query.result.results).toHaveLength(D1_RETENTION_BATCH_SIZE);
    expect(query.result.meta.rows_read).toBeLessThan(
      D1_RETENTION_BATCH_SIZE * 20
    );
  }
  expect(await pruneExpiredBundles(env.DB, NOW)).toEqual({
    deleted: 3,
    hasMore: false
  });
});

test('a failed batch preserves earlier progress and fails the scheduled invocation', async () => {
  await seedBundleBacklog(env.DB, D1_RETENTION_BATCH_SIZE + 1, CUTOFF - 1);
  await env.DB.prepare(`CREATE TRIGGER reject_retention_probe BEFORE DELETE ON bundles
    WHEN OLD.bundle_id = '${id(D1_RETENTION_BATCH_SIZE)}'
    BEGIN SELECT RAISE(ABORT, 'injected retention failure'); END`).run();
  const log = vi.spyOn(console, 'error').mockImplementation(() => undefined);
  try {
    await expect(
      worker.scheduled(createScheduledController({ scheduledTime: NOW }), {
        DB: env.DB
      } as Cloudflare.Env)
    ).rejects.toThrow('injected retention failure');
    expect(log).toHaveBeenCalledWith(
      expect.stringContaining('"event":"d1.retention.failed"')
    );
    expect(
      await env.DB.prepare('SELECT COUNT(*) AS count FROM bundles').first()
    ).toEqual({
      count: 1
    });
  } finally {
    await env.DB.prepare('DROP TRIGGER reject_retention_probe').run();
  }
  expect(await pruneExpiredBundles(env.DB, NOW)).toEqual({
    deleted: 1,
    hasMore: false
  });
});

test('scheduled retention prunes expired sessions and Bind Codes and day-old email challenges, never accounts or tokens', async () => {
  const DAY_MS = 24 * 60 * 60 * 1000;
  const hash = (fill: string) => fill.repeat(64);
  await env.DB.batch([
    env.DB.prepare(`INSERT INTO bpp_accounts (
      account_id, email, password_hash, game_account_id, created_at_ms, updated_at_ms
    ) VALUES ('acct-a', 'a@example.com', 'x', 'game-a', 1, 1),
             ('acct-b', 'b@example.com', 'x', NULL, 1, 1)`),
    env.DB.prepare(`INSERT INTO bpp_web_sessions (session_hash, account_id, created_at_ms, expires_at_ms)
      VALUES (?1, 'acct-a', 1, ?3), (?2, 'acct-a', 1, ?4)`).bind(
      hash('1'),
      hash('2'),
      NOW - 1,
      NOW + 1
    ),
    env.DB.prepare(`INSERT INTO bpp_bind_codes (code_hash, account_id, created_at_ms, expires_at_ms)
      VALUES (?1, 'acct-a', 1, ?3), (?2, 'acct-b', 1, ?4)`).bind(
      hash('3'),
      hash('4'),
      NOW - 1,
      NOW + 1
    ),
    env.DB.prepare(`INSERT INTO bpp_email_challenges (
      challenge_id, purpose, email, code_hmac, created_at_ms, expires_at_ms
    ) VALUES ('old', 'register', 'a@example.com', 'h', 1, ?1),
             ('recent', 'reset', 'a@example.com', 'h', 1, ?2)`).bind(
      NOW - DAY_MS - 1,
      NOW - DAY_MS + 1
    )
  ]);

  vi.spyOn(console, 'log').mockImplementation(() => undefined);
  await worker.scheduled(createScheduledController({ scheduledTime: NOW }), {
    DB: env.DB
  } as Cloudflare.Env);

  const keys = async (sql: string) =>
    (await env.DB.prepare(sql).all<{ key: string }>()).results.map(
      ({ key }) => key
    );
  expect(
    await keys('SELECT session_hash AS key FROM bpp_web_sessions')
  ).toEqual([hash('2')]);
  expect(await keys('SELECT code_hash AS key FROM bpp_bind_codes')).toEqual([
    hash('4')
  ]);
  expect(
    await keys('SELECT challenge_id AS key FROM bpp_email_challenges')
  ).toEqual(['recent']);
  expect(
    await keys('SELECT account_id AS key FROM bpp_accounts ORDER BY account_id')
  ).toEqual(['acct-a', 'acct-b']);
});
