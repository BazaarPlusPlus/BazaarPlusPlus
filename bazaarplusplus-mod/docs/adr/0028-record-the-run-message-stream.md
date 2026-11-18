# ADR-0028: Record the Run Message Stream as the game sends and receives it

Status: Accepted

## Context

An uploaded Run holds the facts the mod derives: its events (`run_started`, `pvp_combat_recorded`), its final values and its PvP battles. It does not hold the course of the run: which cards each shop offered and at what price, the rerolls, and what the player bought, sold, moved and chose. Run analysis needs that course in full.

The game already carries it. The server sends each change of run state as a `NetMessageGameSim` (a `NetMessageGameStateSync` after a reconnect), and the client sends each player action through one `HttpGameClient` method. Reading those messages as they are, rather than deriving new events from game state, keeps the record complete for any later question and keeps the mod out of the game's rules.

ADR-0001 is about the pulled `IEncounterStateProbe` for the live encounter status. That is another seam; this ADR does not reopen it.

## Decision

1. The desktop Host records the Run Message Stream (root `GLOSSARY.md`): each `NetMessageGameSim`, `NetMessageGameStateSync` and `NetMessageRunInitialized` the client receives, as the game's own MessagePack bytes, and each command the client sends through `HttpGameClient`, as the method name and its arguments in JSON. It does not interpret them.
2. Receive side: a prefix on the `NetMessageDispatchSeam` target, which runs before the game handles the message. It must: the game changes some messages while it handles them (it removes prestige events when it replays a state). ADR-0021 serializes a PvP replay after the game handled its messages, which is right for a replay, since the game plays back what it handled; the stream must hold what the server sent. When an aggregate arrives, the prefix serializes every kept message inside it at once and skips those messages when they pass the seam on their own. Tournament spectate playback is skipped, as for the other patches on this seam. Replay playback hands its messages to the processor directly and does not pass the seam.
3. `NetMessageCombatSim` is not recorded. PvP fights already upload their replay; the PvE fights would add about 230–350 KB gzip per run.
4. Send side: a prefix and a postfix on each `HttpGameClient` command method that exists on the build (`SendInitializeRun`, the other `Send*` actions, `ReestablishSession`). A missing method is skipped and never fails patching. The postfix wraps the returned task, so the response is seen before the game handles it. The response is paired with its command by object identity, and the pairing covers the messages inside an aggregate response; the game's 3-character `MessageId` repeats within a run, so it cannot pair them. The response to `ReestablishSession` never passes the seam, so it is recorded from the task. A command records no outcome: one with no paired message failed, was retried, or was never sent, and the messages alone carry the run state.
5. Storage: each frame is one `run_events` row, kind `stream_message` or `stream_command`. No column changes, so the local history database version and the installer contract stay as they are, and deleting a run deletes its rows. Frames that arrive before the run's start event is written (the `SendInitializeRun` command and its answer, or a resumed run's reconnect state) wait in memory and are written right after it; `SendInitializeRun` drops the frames held from before it. Writes go through the queued run-log store, off the main thread.
6. The rows exist only to be uploaded, about 100–150 KB per run. Each seal pass deletes the rows of every Run that can no longer upload them: not active, its Bundle not owed, and no pending Bundle that a reseal would rebuild from them. Most Runs are never sealed (Normal mode, PTR, abandoned), so their rows go at the first pass after they end.
7. Upload: root key `8` of the Run segment, `MessageStream`, is null when nothing was recorded. Three sample runs measured 36–46 KB gzip, against the 2 MiB segment limit and a 416 KB average segment, so the stream is not cut down. A segment over the limit loses the whole stream after its non-projection replays and before its projected battles (degradation category `message_stream_omitted`): part of a stream cannot be followed, and the projected battles serve Ghost Battles.
8. Every reader of a Bundle gets the stream: BazaarDB (`GET /bazaardb/bundles`, only for players who turn on BazaarDB upload) and the opponents who download it as a Ghost Battle (`GET /ghost-battles`). Both routes hand out a presigned URL to the stored Bundle, and the server never rewrites one, so leaving the stream out for them needs a second stored copy of every Bundle. The stream carries the kind of data the Run already gives them: the run's own game state, as in the Battle replays, and the display names of PvP opponents the run met, as in the Battle facts. A copy kept after its Bundle expires follows the README rule like any battle data: account ids and display names come out first, including those inside the game's messages.
9. The Android Host records nothing: its IL2CPP command patches would have no device proof. Its Run segments carry `MessageStream` null.

## Consequences

- An uploaded desktop Run carries its full course: each shop's offers and prices, each choice and each player action, in the order they happened.
- Each Ghost Battle download is 36–46 KB larger. Older mods, the analyzer and the bot decode the longer root array and ignore key `8`; the bot skips it while it decodes.
- A game update that renames a command method drops that command from the stream without breaking the patch; a new command method is not recorded until it is added.
- A crash can lose the queued writes that had not reached the database. A run resumed after a restart continues from the game's reconnect state.
- Local History cannot show the stream: it is gone once the run's Bundle is uploaded or the run is found unsealable.
- The contract is `docs/contracts/run-payload-v6.md`.
