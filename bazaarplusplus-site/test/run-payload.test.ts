// @vitest-environment node

import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { gzipSync } from 'node:zlib';

import { describe, expect, test } from 'vite-plus/test';

import { decodeRunPayload } from '../src/features/runs/run-payload';
import {
  SAMPLE_RUN_ID,
  gzipRun,
  sampleRunPayload
} from './fixtures/run-payload';

const GOLDEN = resolve(
  import.meta.dirname,
  '../../bazaarplusplus-mod/tests/BundleV6Codec.Tests/fixtures/run-payload-v6.fixture.b64'
);

function golden(): Uint8Array {
  return new Uint8Array(
    Buffer.from(readFileSync(GOLDEN, 'utf8').trim(), 'base64')
  );
}

describe('Run payload decoder', () => {
  test('decodes the shared golden', async () => {
    const run = await decodeRunPayload(golden(), 'payload-run');
    expect({ runId: run.runId, hero: run.hero, battles: run.battles }).toEqual({
      runId: 'payload-run',
      hero: 'Vanessa',
      battles: [
        {
          id: 'battle-000',
          source: 'battle',
          hour: 1,
          encounterId: 'encounter-final',
          recordedAtMs: 1785716940000,
          day: 10,
          pvp: true,
          outcome: 'win',
          opponentHero: 'Pygmalien',
          hand: {
            status: 'captured',
            cards: [
              {
                instanceId: 'instance-0',
                templateId: '00000000-0000-0000-0000-000000000001',
                type: 1,
                size: 2,
                slot: 4,
                name: 'Golden card 0',
                tier: 'Gold',
                enchant: 'Burning'
              }
            ]
          },
          skills: {
            status: 'captured',
            cards: [
              {
                instanceId: 'instance-1',
                templateId: '00000000-0000-0000-0000-000000000002',
                type: 2,
                size: 1,
                slot: 5,
                name: 'Golden card 1',
                tier: 'Silver',
                enchant: null
              }
            ]
          }
        }
      ]
    });
    // Key 8 keeps all six fields of every frame, the message as its bytes.
    expect(run.platform).toBe('desktop');
    expect(run.streamOmitted).toBe(false);
    expect(run.cardNames.get('00000000-0000-0000-0000-000000000001')).toBe(
      'Golden card 0'
    );
    expect(
      run.stream?.map((f) =>
        Object.assign({}, f, { message: f.message?.length ?? null })
      )
    ).toEqual([
      {
        seq: 3,
        timestampMs: 1785716000000,
        message: null,
        respondsToSeq: null,
        command: 'SendSelectItem',
        commandArgsJson:
          '{"instanceId":"itm_golden","desiredSockets":["Socket_3"],"targetSection":"Hand"}'
      },
      {
        seq: 4,
        timestampMs: 1785716000250,
        message: 300,
        respondsToSeq: 3,
        command: null,
        commandArgsJson: null
      }
    ]);
  });

  test('orders Battles by time and reads a Missing hand', async () => {
    const run = await decodeRunPayload(sampleRunPayload(), SAMPLE_RUN_ID);
    expect(run.battles.map((b) => [b.day, b.source, b.pvp, b.outcome])).toEqual(
      [
        [1, 'battle', true, 'loss'],
        [2, 'battle', false, 'win'],
        [3, 'battle', true, 'win'],
        [4, 'pve-event', false, 'win'],
        [5, 'battle', true, 'win'],
        [8, 'battle', true, 'win'],
        [11, 'battle', true, 'loss']
      ]
    );
    expect(run.battles[3]).toMatchObject({
      encounterId: 'enc-tide-turner',
      hand: { status: 'missing' },
      skills: { status: 'missing' }
    });
    expect(run.battles[1].hand).toEqual({ status: 'missing', cards: [] });
    expect(run.battles[0].skills).toEqual({ status: 'empty', cards: [] });
  });

  test('rejects another Run id', async () => {
    await expect(decodeRunPayload(golden(), 'other-run')).rejects.toThrow(
      'Run id mismatch'
    );
  });

  test('rejects another payload version', async () => {
    await expect(
      decodeRunPayload(gzipRun([5, 'r', 'a', ['Mak'], null, []]), 'r')
    ).rejects.toThrow('Unsupported Run payload version');
  });

  test('rejects input that is not gzip', async () => {
    await expect(
      decodeRunPayload(new Uint8Array([0x96, 6]), 'r')
    ).rejects.toThrow('Run is not gzip');
  });

  test('stops reading past 64 MiB decompressed', async () => {
    const bomb = new Uint8Array(gzipSync(new Uint8Array(64 * 1024 * 1024 + 1)));
    await expect(decodeRunPayload(bomb, 'r')).rejects.toThrow(
      'Run exceeds 64 MiB'
    );
  });
});
