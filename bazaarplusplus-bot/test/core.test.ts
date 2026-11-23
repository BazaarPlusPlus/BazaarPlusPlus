import { Buffer } from 'node:buffer';
import { beforeAll, expect, test } from 'vitest';
import encoded from '../../bazaarplusplus-mod/tests/BundleV6Codec.Tests/fixtures/run-payload-v6.fixture.b64?raw';
import projection from '../../bazaarplusplus-analyzer/tests/fixtures/run-payload-v6.projection.json';
import { decodeRun } from '../src/bundle';
import { MessagePack } from '../src/msgpack';
import { parseCommand, queryWindow, type Names } from '../src/commands';
import { accepted, finalCards, summarize } from '../src/stats';
import { statsHtml } from '../src/report';
import type { Run } from '../src/model';
const names: Names = {
  heroes: { Vanessa: '瓦内莎', TheDragons: '双龙' },
  names: {}
};
let sample: Run;
beforeAll(async () => {
  sample = await decodeRun(
    Buffer.from(encoded.trim(), 'base64'),
    'payload-run',
    'golden-account'
  );
});
test('decodes the mod golden into the analyzer-owned projection', () => {
  const e = projection.runs[0];
  const b = projection.battles[0];
  expect(sample).toMatchObject({
    id: e.run_id,
    account: e.player_account_id,
    hero: e.hero,
    rank: e.final_rank,
    status: e.status,
    ended: Date.parse(e.ended_at_utc),
    wins: e.victories,
    rating: e.final_rating,
    delta: e.final_rating_delta
  });
  expect(sample.battles[0]).toMatchObject({
    id: b.battle_id,
    winner: b.winner_side,
    opponent: b.opponent_hero,
    pvp: true,
    final: true
  });
  // The shared fixture intentionally uses card_type=1, not a board Item (0).
  expect(finalCards(sample)).toHaveLength(0);
});
test('rejects gzip truncation, wrong identity and non-gzip input', async () => {
  const data = Buffer.from(encoded.trim(), 'base64');
  await expect(decodeRun(data, 'other', 'golden-account')).rejects.toThrow(
    'identity'
  );
  await expect(
    decodeRun(data.subarray(0, 40), 'payload-run', 'golden-account')
  ).rejects.toThrow();
  await expect(decodeRun(new Uint8Array([1, 2]), '', '')).rejects.toThrow(
    'gzip'
  );
});
test('skips event bodies and enforces the 64 MiB limit without buffering the payload', async () => {
  let emitted = 0;
  const stream = new ReadableStream<Uint8Array>({
    pull(controller) {
      if (emitted === 0) {
        controller.enqueue(
          new Uint8Array([0x95, 6, 0xc0, 0xc0, 0xc0, 0xc6, 4, 0, 0, 0])
        );
        emitted++;
      } else if (emitted <= 64) {
        controller.enqueue(new Uint8Array(1024 * 1024));
        emitted++;
      } else controller.close();
    }
  });
  await expect(new MessagePack(stream).decode()).rejects.toThrow('64 MiB');
});
test('a bare @ asks for the own stats; the rest of 战绩 is the name', () => {
  expect(parseCommand('<@!123>', names)).toEqual({ kind: '战绩' });
  expect(parseCommand('<@!123> /战绩 张 三', names)).toEqual({
    kind: '战绩',
    name: '张 三'
  });
  expect(parseCommand('战绩 "今天"', names).name).toBe('今天');
  expect(parseCommand('英雄 双龙', names).hero).toBe('TheDragons');
  expect(parseCommand('绑定 瓦内莎', names).name).toBe('瓦内莎');
  expect(() => parseCommand('最近', names)).toThrow('@我');
});
test('Beijing calendar days cross the UTC date boundary', () => {
  const now = Date.parse('2026-11-16T00:01:00+08:00');
  expect(queryWindow(now)).toEqual({
    from: Date.parse('2026-11-14T00:00:00+08:00'),
    before: now
  });
});
test('unknown outcomes do not become zero; only decided PvP battles enter win rate', () => {
  const run = {
    ...sample,
    wins: 10,
    losses: 0,
    battles: [
      ...sample.battles,
      { ...sample.battles[0], pvp: false, winner: 'opponent' as const },
      { ...sample.battles[0], winner: null }
    ]
  };
  const unknown = {
    ...sample,
    wins: null,
    losses: null,
    initialRating: null,
    rating: null,
    battles: []
  };
  expect(summarize([run, unknown])).toMatchObject({
    runs: 2,
    tenWins: 1,
    perfect: 1,
    wins: 1,
    losses: 0,
    meanWins: 10
  });
  expect(accepted({ ...sample, rank: null })).toBe(false);
  expect(accepted({ ...sample, status: 'abandoned' })).toBe(false);
  expect(
    finalCards({ ...sample, battles: [...sample.battles, ...sample.battles] })
  ).toBeNull();
});
test('rendering escapes user text and labels partial reports', () => {
  const html = statsHtml({
    name: '<img src=x onerror=alert(1)>',
    runs: [{ ...sample, rating: null, initialRating: null }],
    names,
    from: sample.ended! - 1,
    before: sample.ended! + 1,
    art: {},
    skipped: 2
  });
  expect(html).toContain('&lt;img');
  expect(html).not.toContain('<img src=x');
  expect(html).toContain('<b>传奇</b><small>段位</small>');
  expect(html).toContain('BazaarPlusPlus');
  expect(html).toContain('不完整统计');
});

test.each(['Master', 'Masters'])('renders %s as 大师', (rank) => {
  const run = { ...sample, rank, rating: null, initialRating: null };
  expect(accepted(run)).toBe(true);
  const html = statsHtml({
    name: 'Fixture',
    runs: [run],
    names,
    from: sample.ended! - 1,
    before: sample.ended! + 1,
    art: {},
    skipped: 0
  });
  expect(html).toContain('<b>大师</b><small>段位</small>');
});
