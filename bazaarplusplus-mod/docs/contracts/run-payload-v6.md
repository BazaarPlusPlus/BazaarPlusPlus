# Run payload V6 contract

The Run segment inside a V6 Bundle uses media type `application/x-bpp-run-v6`. Its bytes are a
gzip stream containing one MessagePack `RunPayloadV6` object. Bundle validation, segment length,
and the compressed-segment SHA-256 are handled before this payload is decompressed.

## Compatibility

- `payload_format_version` is `6` (`RunPayloadV6.PayloadFormatVersion`, MessagePack key `0`).
- The serialized graph is public because it is decoded in the Unity/Mono runtime.
- Every DTO is encoded as a MessagePack array with the numeric keys declared by its `[Key]`
  attributes. Existing keys are immutable. Additive fields must use new trailing keys.
- A decoder must reject a version other than `6`, a non-gzip input, malformed MessagePack, or a
  decompressed payload larger than 64 MiB.
- `RunId` and `PlayerAccountId` must equal the enclosing Bundle manifest before any Battle replay is
  consumed.

## Changes from V5

V5 is frozen in [run-payload-v5.md](run-payload-v5.md). V6 keeps every key and type except:

- Every time is a Unix millisecond integer (MessagePack int): `RunFactsV6` keys `3` and `4`
  (`StartedAtMs`, nullable `EndedAtMs`), `RunEventV6` key `1` (`TimestampMs`), and
  `BattleFactsV6` key `0` (`RecordedAtMs`). V5 carried round-trip ISO-8601 text there.
- `RunEventV6.PayloadJson` no longer repeats `run_id`, `seq`, `ts`, or `kind`: the Run root and
  the event's keys `0..2` own them, and every consumer reads them from there. The JSON keeps the
  remaining fields of the stored event in stored order, including `schema_version`
  (`RunLogEvent.PayloadSchemaVersion`); `RunPayloadComposer.EventPayloadBody` does the stripping.

## Golden

The stable encoded sample is
`tests/BundleV6Codec.Tests/fixtures/run-payload-v6.fixture.b64`. The mod codec test decodes this gzip
fixture and pins its decompressed MessagePack bytes (`BPP_UPDATE_GOLDENS=1` rewrites it); gzip
headers and deflate output can vary by OS/runtime. The analyzer reads the same file and checks its
Run, Battle, card, and quality rows against its
[expected projection](../../../bazaarplusplus-analyzer/tests/fixtures/run-payload-v6.projection.json).
Bundle envelope goldens are owned by the [server contract fixtures](../../../bazaarplusplus-server/contracts/v6/fixtures/);
the mod test project links those files directly.

## Root object

| Key | Property | Type | Meaning |
|---:|---|---|---|
| 0 | `PayloadFormatVersion` | int | Always `6` |
| 1 | `RunId` | string | Stable local Run identity |
| 2 | `PlayerAccountId` | string | Frozen Run player identity |
| 3 | `Run` | `RunFactsV6` | Final Run facts |
| 4 | `Events` | `RunEventV6[]` | Run events ordered by sequence |
| 5 | `Battles` | `RunBattleV6[]` | Battle facts and optional replay inputs |
| 6 | `ReplayableBattleIds` | string[] | Battles retaining snapshots and all replay phases |
| 7 | `Degradation` | `PayloadDegradationV6` | Deterministic size/input degradation record |

## Nested objects

`RunFactsV6` records hero, mode, seed, start/end time (Unix ms), terminal status, day/hour, wins/losses,
starting and final rank/rating, rating delta, final economy/build values, build channel, and mod
version. Its keys are `0..21` in property declaration order.

`RunEventV6` uses keys `0..3` for sequence, Unix-ms timestamp, kind, and the event's structured
JSON without the envelope keys above. The JSON is text and the Bundle pipeline does not otherwise
reinterpret it.

`RunBattleV6` uses keys `0..4` for Battle ID, `BattleFactsV6` (key `0` is the Unix-ms recorded
time), `BattleParticipantsV6`, nullable `BattleCardSnapshotsV6`, and nullable `BattleReplayV6`. A Battle is replayable only when both
nullable fields exist, Spawn, Combat, and Despawn replay byte arrays are non-empty, and snapshots
contain exactly one non-`Missing` set for each of `player_hand`, `player_skills`, `opponent_hand`,
and `opponent_skills`, with no duplicate or extra set. `RunBundleV6Contract` owns this predicate on
both composition and ghost-import paths.

`BattleParticipantV6` carries account/display/hero/rank/rating/level/prestige/victories plus
payload-only income, gold, hand-item count, and skill count. Manifest projection normalization is a
separate operation and does not copy nullable payload values blindly.

`BattleCardSnapshotsV6` contains the four captured card sets. `BattleCardV6` preserves stable
template and instance identity, type/size/location, display metadata, tags, and integer attributes.

`BattleReplayV6` uses keys `0..3` for replay version and the native Spawn, Combat, and Despawn
MessagePack byte sequences.

`PayloadDegradationV6` uses keys `0..3` for categories, replay-omitted Battle IDs, omitted event
count, and whether a requested Screenshot was omitted. It describes the final immutable payload;
it is not a retry state.
