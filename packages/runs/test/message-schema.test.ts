// @vitest-environment node

import { execFileSync } from 'node:child_process';
import { existsSync, mkdtempSync, readdirSync, readFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';

import { describe, expect, test } from 'vite-plus/test';

import type { GameMessageSchema, SchemaSource } from '../message-stream/schema';
import {
  SCHEMA_PATH,
  snapshotSource,
  schemaProblems
} from '../tools/message-schema/generate';
import {
  EMPTY_SCHEMA,
  mergeSource,
  serializeSchema,
  type Extraction
} from '../tools/message-schema/merge';

const NS = 'BazaarGameShared.Infra.Messages';
const ROOT = `${NS}.INetMessage`;

function source(
  id: string,
  layout: 'keyed' | 'inferred' = 'keyed'
): SchemaSource {
  return {
    id,
    gameVersion: `1.0.${id}`,
    platform: layout === 'keyed' ? 'macos' : 'android',
    layout
  };
}

function keyed(members: Array<[number, string, string]>): Extraction {
  return {
    assembly: 'BazaarGameShared',
    mode: 'attributes',
    types: {
      [ROOT]: { kind: 'union', cases: [[1, `${NS}.NetMessageError`]] },
      [`${NS}.NetMessageError`]: { kind: 'object', members },
      [`${NS}.ERank`]: {
        kind: 'enum',
        underlying: 'System.Int32',
        flags: false,
        values: [['Bronze', '0']]
      }
    }
  };
}

const A = keyed([
  [0, 'MessageId', 'System.String'],
  [2, 'Message', 'System.String']
]);
const B = keyed([
  [0, 'MessageId', 'System.String'],
  [2, 'Message', 'System.String'],
  [3, 'Detail', 'System.Nullable<BazaarGameShared.Domain.Core.InstanceId>']
]);
const C = keyed([
  [0, 'MessageId', 'System.String'],
  [2, 'Text', 'System.String']
]);

const merge = (...steps: Array<[SchemaSource, Extraction]>) =>
  steps.reduce((schema, [s, e]) => mergeSource(schema, s, e), EMPTY_SCHEMA);

describe('Game Message Schema merge', () => {
  test('keyed builds merge in any order to the same bytes, and again changes nothing', () => {
    const ab = merge([source('1'), A], [source('2'), B]);
    const ba = merge([source('2'), B], [source('1'), A]);
    expect(serializeSchema(ab)).toBe(serializeSchema(ba));
    expect(serializeSchema(mergeSource(ab, source('2'), B))).toBe(
      serializeSchema(ab)
    );
    const error = ab.types[`${NS}.NetMessageError`];
    expect(error.kind === 'object' && error.members).toEqual([
      { key: 0, name: 'MessageId', type: 'System.String' },
      { key: 2, name: 'Message', type: 'System.String' },
      {
        key: 3,
        name: 'Detail',
        type: 'System.Nullable<BazaarGameShared.Domain.Core.InstanceId>',
        sources: ['2']
      }
    ]);
  });

  test('a key with another name in another build is kept as both, not chosen', () => {
    const schema = merge([source('1'), A], [source('3'), C]);
    const error = schema.types[`${NS}.NetMessageError`];
    expect(
      error.kind === 'object' && error.members.filter((m) => m.key === 2)
    ).toEqual([
      { key: 2, name: 'Message', type: 'System.String', sources: ['1'] },
      { key: 2, name: 'Text', type: 'System.String', sources: ['3'] }
    ]);
  });

  test('an inferred build adds its source by name and never places a member', () => {
    const android: Extraction = {
      assembly: 'Il2CppBazaarGameShared',
      mode: 'names',
      types: {
        [`${NS}.NetMessageError`]: {
          kind: 'object',
          members: [
            ['Message', 'System.String'],
            ['MessageId', 'System.String'],
            ['Extra', 'System.Int32']
          ]
        }
      }
    };
    expect(() => merge([source('a', 'inferred'), android])).toThrow(
      'Merge a keyed'
    );
    const schema = merge([source('1'), A], [source('a', 'inferred'), android]);
    const error = schema.types[`${NS}.NetMessageError`];
    expect(error).toMatchObject({
      sources: ['1', 'a'],
      members: [
        { key: 0, name: 'MessageId' },
        { key: 2, name: 'Message' }
      ],
      unkeyed: [{ name: 'Extra', type: 'System.Int32', sources: ['a'] }]
    });
    // A later keyed build that places the member takes the inferred source along.
    const placed = mergeSource(
      schema,
      source('4'),
      keyed([[5, 'Extra', 'System.Int32']])
    );
    const after = placed.types[`${NS}.NetMessageError`];
    expect(after.kind === 'object' && after.unkeyed).toBeUndefined();
    expect(
      after.kind === 'object' && after.members.find((m) => m.name === 'Extra')
    ).toEqual({
      key: 5,
      name: 'Extra',
      type: 'System.Int32',
      sources: ['4', 'a']
    });
  });

  test('names a snapshot source by platform and build', () => {
    expect(
      snapshotSource('macos-online-x', {
        platform: 'macos',
        gameVersion: '1.0.13176-prod-macos-arm64-2b11c7ea'
      })
    ).toEqual({
      id: 'macos-13176',
      gameVersion: '1.0.13176-prod-macos-arm64-2b11c7ea',
      platform: 'macos',
      layout: 'keyed'
    });
    expect(
      snapshotSource('android-play-13250', {
        platform: 'android',
        gameVersion: '1.0.9',
        versionCode: 13250
      }).id
    ).toBe('android-13250');
  });
});

describe('Game Message Schema check', () => {
  const schema = merge([source('13176'), A], [source('13295'), B]);

  test('passes on what the generator writes', () => {
    expect(schemaProblems(serializeSchema(schema))).toEqual([]);
  });

  test('fails on a hand edit or a source it does not record', () => {
    expect(schemaProblems(JSON.stringify(schema))).toEqual([
      'the schema is not what the generator writes'
    ]);
    const unknown = structuredClone(schema);
    unknown.sources = unknown.sources.filter((s) => s.id !== '13295');
    expect(schemaProblems(serializeSchema(unknown))).toEqual([
      'source 13295 is not recorded'
    ]);
  });

  test('the committed schema passes', () => {
    expect(schemaProblems(readFileSync(SCHEMA_PATH, 'utf8'))).toEqual([]);
  });
});

describe('Game Message Schema generator', () => {
  const committed = readFileSync(SCHEMA_PATH, 'utf8');
  const recorded = (JSON.parse(committed) as GameMessageSchema).sources;
  const gameLibs = resolve(
    import.meta.dirname,
    '../../../bazaarplusplus-mod/game-libs'
  );
  // The first recorded desktop snapshot that is on disk, if any.
  const found = (existsSync(gameLibs) ? readdirSync(gameLibs) : [])
    .map((name) => join(gameLibs, name))
    .flatMap((dir) => {
      const manifest = join(dir, 'manifest.json');
      if (!existsSync(manifest)) return [];
      const { gameVersion } = JSON.parse(readFileSync(manifest, 'utf8')) as {
        gameVersion: string;
      };
      const match = recorded.find(
        (s) => s.layout === 'keyed' && s.gameVersion === gameVersion
      );
      const dll = join(dir, 'Managed/BazaarGameShared.dll');
      return match && existsSync(dll) ? [{ source: match, dll }] : [];
    })[0];

  // Needs a recorded snapshot and the .NET SDK, so it runs where both are present.
  test.skipIf(!found)(
    'reading a recorded snapshot again leaves the committed schema unchanged',
    () => {
      const out = join(mkdtempSync(join(tmpdir(), 'bpp-schema-')), 'x.json');
      execFileSync(
        'dotnet',
        [
          'run',
          resolve(import.meta.dirname, '../tools/message-schema/extract.cs'),
          '--',
          found.dll,
          'attributes',
          out
        ],
        { stdio: 'ignore' }
      );
      const extraction = JSON.parse(readFileSync(out, 'utf8')) as Extraction;
      // Qualified names keep the two ERank types and nested generic arguments apart.
      expect(Object.keys(extraction.types)).toContain(
        'BazaarGameShared.TempoNet.Enums.ERank'
      );
      // Read-only assertions: the extraction is merged below exactly as extracted.
      const aura =
        extraction.types[
          'BazaarGameShared.Infra.Messages.CombatSimEvents.CombatSimEventEffectAuraExecuted'
        ];
      const members = aura.kind === 'object' ? aura.members : [];
      expect(members).toContainEqual([
        4,
        'AppliedTo',
        'System.Collections.Generic.HashSet<BazaarGameShared.Infra.Messages.Shared.IEffectTarget>'
      ]);
      expect(members).toContainEqual([
        2,
        'Source',
        'System.Nullable<BazaarGameShared.Domain.Core.InstanceId>'
      ]);
      const schema = JSON.parse(committed) as GameMessageSchema;
      expect(
        serializeSchema(mergeSource(schema, found.source, extraction))
      ).toBe(committed);
    },
    120_000
  );
});
