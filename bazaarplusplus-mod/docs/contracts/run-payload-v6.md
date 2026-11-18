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
| 8 | `MessageStream` | `RunMessageFrameV6[]` or null | The Run Message Stream; absent before the key existed |

Every time is a Unix millisecond integer (MessagePack int): `RunFactsV6` keys `3` and `4`
(`StartedAtMs`, nullable `EndedAtMs`), `RunEventV6` key `1` (`TimestampMs`), and `BattleFactsV6`
key `0` (`RecordedAtMs`).

## Nested objects

`RunFactsV6` records hero, mode, seed, start/end time, terminal status, day/hour, wins/losses,
starting and final rank/rating, rating delta, final economy/build values, build channel, mod
version, and the game version the Run started under. Its keys are `0..23` in property declaration
order. Keys `22` and `23` are the game version, for statistics split by game patch; each is `null`
when it was not read, and absent from a payload sealed before the keys existed:

- `22` `ServerBuildId`: the `BuildId` the game server sends in its run-initialized message
  (`NetMessageRunInitialized`), as text. It is not the Steam build id or the client version.
- `23` `GameDataEtag`: the ETag of the `GameData.db` the game had loaded, from the manifest the
  game keeps beside that file, without its HTTP quotes: the form `gameDataEtag` in
  `release/card-art.ts` reads from the public GameData URL. The Android Host does not read it yet.

Both come from the Run's `run_started` event; `RunPayloadComposer` reads them from the stored
events, before the size budget can trim any.

`RunEventV6` uses keys `0..3` for sequence, timestamp, kind, and the structured event JSON.
`PayloadJson` does not repeat `run_id`, `seq`, `ts`, or `kind`: the Run root and the event's keys
`0..2` own them, and every consumer reads them from there. The JSON keeps the remaining fields of
the stored event in stored order; the events carry no version of their own, the run format
version (`BundleLimitsV6.RunFormatVersion`) covers them.
`RunPayloadComposer.EventPayloadBody` does the stripping. The Bundle pipeline does not reinterpret it.
A `run_started` or `run_resumed` event also carries `server_build_id` and `game_data_etag`, the
values above as read when that event was written; a field that was not read is omitted. A resumed
Run can carry a newer game version than its `run_started` event.

Rows of kind `stream_message` and `stream_command` are not events: they make up key `8`, below.

`RunBattleV6` uses keys `0..4` for Battle ID, `BattleFactsV6`, `BattleParticipantsV6`, nullable
`BattleCardSnapshotsV6`, and nullable `BattleReplayV6`. A Battle is replayable only when both
nullable fields exist, Spawn, Combat, and Despawn replay byte arrays are non-empty, and snapshots
contain exactly one non-`Missing` set for each of `player_hand`, `player_skills`, `opponent_hand`,
and `opponent_skills`, with no duplicate or extra set. `RunBundleV6Contract` owns this predicate on
both composition and ghost-import paths.

`BattleParticipantV6` carries account/display/hero/rank/rating/level/prestige/victories plus
payload-only income, gold, hand-item count, and skill count. The counts (keys `10` and `11`) are the
number of `Item`-type cards in that side's `*_hand` set and of `Skill`-type cards in its `*_skills`
set, and are `null` when that set is `Missing`. `RunPayloadComposer` counts them before the size
budget runs, so they remain when trimming drops a Battle's snapshots. Manifest projection
normalization is a separate operation and does not copy nullable payload values blindly.

`BattleCardSnapshotsV6` contains the four captured card sets. `BattleCardV6` preserves stable
template and instance identity, type/size/location, display metadata, tags, and integer attributes.
`Size` is the size the card held on the board, not its template's: a Tiny item (`GLOSSARY.md`) keeps
its `TemplateId`, records a `Size` one step below its template's, and carries `Tiny` = 1 in its
attributes. A consumer takes the card's width from `Size`.

`BattleReplayV6` uses keys `0..3` for replay version and the native Spawn, Combat, and Despawn
MessagePack byte sequences. Key `4`, `PlayerAttributes`, is the recording player's attributes as the
combat opened, keyed by the game's `EPlayerAttributeType` member name. The Spawn message carries
only the attributes that changed for that player (the game merges them into its own state), so the
map is the game's live player state with the Spawn message's player changes already applied; a
consumer does not overlay them again. It is `null` when the recording host does not read it (the
Android Host) or the read failed, and absent from a payload sealed before the key existed. It does
not take part in the replayable predicate.

`PayloadDegradationV6` uses keys `0..3` for categories, replay-omitted Battle IDs, omitted event
count, and whether a requested Screenshot was omitted. The size budget drops, in order,
non-projection replays, the whole Run Message Stream (`message_stream_omitted`), projected Battles,
snapshots without a replay, and non-terminal events. It describes the final immutable payload;
it is not a retry state.

## Run Message Stream

Key `8` holds what the game received and sent during the Run, in the order the mod observed it
(ADR-0028): the game's own messages, for following the Run step by step. It is null when nothing
was recorded (the Android Host, or a Run before the key existed), and dropped whole when the segment
is over the size limit (`message_stream_omitted` below). `RunMessageFrameV6` uses keys `0..5`; each
frame sets exactly one of `Message` and `Command`:

| Key | Property | Type | Meaning |
|---:|---|---|---|
| 0 | `Seq` | int | The frame's `run_events` sequence; it orders the frames, and shares its numbers with key `4` |
| 1 | `TimestampMs` | int | When the mod observed the frame; a frame written after the start event can be earlier than it |
| 2 | `Message` | bin or null | One message the client received, serialized as `INetMessage` with the game's `MessagePackConfig.Options`: a `NetMessageGameSim`, `NetMessageGameStateSync` or `NetMessageRunInitialized`. An aggregate is split into its messages. Read it with the game's types for the Run's `ServerBuildId` |
| 3 | `RespondsToSeq` | int or null | On a message: the `Seq` of the command whose response carried it; null for a message the server sent on its own, or a response the mod could not pair |
| 4 | `Command` | string or null | A command the client sent: the `HttpGameClient` method, such as `SendSelectItem` |
| 5 | `CommandArgsJson` | string or null | On a command: its arguments as a JSON object by parameter name. Instance ids are their text, enums their member name, lists arrays |

A command has no outcome field. A command no message answers failed, was retried, or was never
sent; a retry appears as a second command. The messages alone carry the Run state: a
`NetMessageGameSim` carries only what changed, and a `NetMessageGameStateSync` (after a reconnect)
the whole state.

## Golden

The stable encoded sample is
`tests/BundleV6Codec.Tests/fixtures/run-payload-v6.fixture.b64`. The mod codec test decodes this gzip
fixture and pins its decompressed MessagePack bytes (`BPP_UPDATE_GOLDENS=1` rewrites it); gzip
headers and deflate output can vary by OS/runtime. The analyzer reads the same file and checks its
Run, Battle, card, and quality rows against its
[expected projection](../../../bazaarplusplus-analyzer/tests/fixtures/run-payload-v6.projection.json).
Bundle envelope goldens are owned by the [server contract fixtures](../../../bazaarplusplus-server/contracts/v6/fixtures/);
the mod test project links those files directly.
