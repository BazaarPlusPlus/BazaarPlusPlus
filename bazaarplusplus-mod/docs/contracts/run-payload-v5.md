# Run payload V5 contract

The Run segment inside a V5 Bundle uses media type `application/x-bpp-run-v5`. Its bytes are a
gzip stream containing one MessagePack `RunPayloadV5` object. Bundle validation, segment length,
and the compressed-segment SHA-256 are handled before this payload is decompressed.

## Compatibility

- `payload_format_version` is `5` (`RunPayloadV5.PayloadFormatVersion`, MessagePack key `0`).
- The serialized graph is public because it is decoded in the Unity/Mono runtime.
- Every DTO is encoded as a MessagePack array with the numeric keys declared by its `[Key]`
  attributes. Existing keys are immutable. Additive fields must use new trailing keys.
- A decoder must reject a version other than `5`, a non-gzip input, malformed MessagePack, or a
  decompressed payload larger than 64 MiB.
- `RunId` and `PlayerAccountId` must equal the enclosing Bundle manifest before any Battle replay is
  consumed.

The stable encoded sample is
`tests/BundleV5Codec.Tests/fixtures/run-payload-v5.fixture.b64`. The mod codec test decodes this gzip
fixture and pins its decompressed MessagePack bytes; gzip headers and deflate output can vary by
OS/runtime. The analyzer reads the same file and checks its Run, Battle, card, and quality rows against its
[expected projection](../../../bazaarplusplus-analyzer/tests/fixtures/run-payload-v5.projection.json).
Bundle envelope goldens are owned by the [server contract fixtures](../../../bazaarplusplus-server/contracts/v5/fixtures/);
the mod test project links those files directly.

## Root object

| Key | Property | Type | Meaning |
|---:|---|---|---|
| 0 | `PayloadFormatVersion` | int | Always `5` |
| 1 | `RunId` | string | Stable local Run identity |
| 2 | `PlayerAccountId` | string | Frozen Run player identity |
| 3 | `Run` | `RunFactsV5` | Final Run facts |
| 4 | `Events` | `RunEventV5[]` | Run events ordered by sequence |
| 5 | `Battles` | `RunBattleV5[]` | Battle facts and optional replay inputs |
| 6 | `ReplayableBattleIds` | string[] | Battles retaining snapshots and all replay phases |
| 7 | `Degradation` | `PayloadDegradationV5` | Deterministic size/input degradation record |

## Nested objects

`RunFactsV5` records hero, mode, seed, start/end time, terminal status, day/hour, wins/losses,
starting and final rank/rating, rating delta, final economy/build values, build channel, and mod
version. Its keys are `0..21` in property declaration order.

`RunEventV5` uses keys `0..3` for sequence, UTC timestamp, kind, and the original structured JSON.
The JSON is retained as text and is not reinterpreted by the Bundle pipeline.

`RunBattleV5` uses keys `0..4` for Battle ID, `BattleFactsV5`, `BattleParticipantsV5`, nullable
`BattleCardSnapshotsV5`, and nullable `BattleReplayV5`. A Battle is replayable only when both
nullable fields exist, Spawn, Combat, and Despawn replay byte arrays are non-empty, and snapshots
contain exactly one non-`Missing` set for each of `player_hand`, `player_skills`, `opponent_hand`,
and `opponent_skills`, with no duplicate or extra set. `RunBundleV5Contract` owns this predicate on
both composition and ghost-import paths.

`BattleParticipantV5` carries account/display/hero/rank/rating/level/prestige/victories plus
payload-only income, gold, hand-item count, and skill count. Manifest projection normalization is a
separate operation and does not copy nullable payload values blindly.

`BattleCardSnapshotsV5` contains the four captured card sets. `BattleCardV5` preserves stable
template and instance identity, type/size/location, display metadata, tags, and integer attributes.

`BattleReplayV5` uses keys `0..3` for replay version and the native Spawn, Combat, and Despawn
MessagePack byte sequences.

`PayloadDegradationV5` uses keys `0..3` for categories, replay-omitted Battle IDs, omitted event
count, and whether a requested Screenshot was omitted. It describes the final immutable payload;
it is not a retry state.
