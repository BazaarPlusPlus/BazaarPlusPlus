"""Streaming projection from verified Bundle V5 objects to five Arrow tables."""

import gzip
import hashlib
import json
import struct
from collections.abc import Iterable, Iterator, Mapping, Sequence
from dataclasses import dataclass
from datetime import UTC, datetime, timedelta
from io import BytesIO
from typing import Any, cast

import pyarrow as pa

from bppanalyzer.accepted_runs import (
    CANONICAL_HEROES,
    CANONICAL_RANKS,
    RunAdmission,
    admit_run,
    normalize_hero,
)
from bppanalyzer.bundle_source import Bundle, RawHourIndex

PROJECTION_VERSION = "v5-consumer-contract-1"
MAX_DECOMPRESSED_RUN_BYTES = 64 * 1024 * 1024
ROW_BATCH_SIZE = 50_000


class ProjectionError(RuntimeError):
    """The complete Source Hour could not be projected consistently."""


class RunPayloadError(ValueError):
    """A stable, deterministic reason to quarantine one Bundle."""

    def __init__(self, reason: str, message: str) -> None:
        super().__init__(message)
        self.reason = reason


class HourProjection:
    """One hour's metadata plus either reusable tables or a one-shot batch stream."""

    __slots__ = (
        "source_hour",
        "raw_commit_sha256",
        "projection_version",
        "_tables",
        "_batch_stream",
        "_stream_consumed",
    )

    def __init__(
        self,
        source_hour: datetime,
        raw_commit_sha256: str,
        tables: Mapping[str, pa.Table] | None = None,
        projection_version: str = PROJECTION_VERSION,
        *,
        batch_stream: "_ProjectedBatchStream | None" = None,
    ) -> None:
        if (tables is None) == (batch_stream is None):
            raise ValueError("Hour Projection requires tables or a batch stream")
        self.source_hour = source_hour
        self.raw_commit_sha256 = raw_commit_sha256
        self.projection_version = projection_version
        self._tables = dict(tables) if tables is not None else None
        self._batch_stream = batch_stream
        self._stream_consumed = False

    @property
    def tables(self) -> Mapping[str, pa.Table]:
        """Materialize Arrow tables for compatibility with small direct consumers."""
        if self._tables is None:
            batches: dict[str, list[pa.RecordBatch]] = {name: [] for name in _SCHEMAS}
            for name, batch in self.iter_batches():
                try:
                    batches[name].append(batch)
                except KeyError as error:
                    raise ProjectionError(f"Projection emitted an unknown table: {name}") from error
            self._tables = {
                name: pa.Table.from_batches(items, schema=_SCHEMAS[name])
                for name, items in batches.items()
            }
        return self._tables

    @property
    def table_names(self) -> tuple[str, ...]:
        return tuple(self._tables) if self._tables is not None else tuple(_SCHEMAS)

    @property
    def schemas(self) -> Mapping[str, pa.Schema]:
        if self._tables is not None:
            return {name: table.schema for name, table in self._tables.items()}
        return table_schemas()

    def iter_batches(self) -> Iterator[tuple[str, pa.RecordBatch]]:
        if self._tables is not None:
            for name, table in self._tables.items():
                for batch in table.to_batches(max_chunksize=ROW_BATCH_SIZE):
                    yield name, batch
            return
        if self._stream_consumed or self._batch_stream is None:
            raise ProjectionError("Streaming Hour Projection was already consumed")
        self._stream_consumed = True
        yield from self._batch_stream

    @property
    def bundle_count(self) -> int:
        if self._tables is not None:
            return self._tables["runs"].num_rows + self._tables["quarantine"].num_rows
        if self._batch_stream is not None and self._batch_stream.bundle_count is not None:
            return self._batch_stream.bundle_count
        return self.tables["runs"].num_rows + self.tables["quarantine"].num_rows


_SCHEMAS: dict[str, pa.Schema] = {
    "runs": pa.schema(
        [
            ("source_hour", pa.string()),
            ("source_day", pa.string()),
            ("available_at_ms", pa.int64()),
            ("bundle_id", pa.string()),
            ("bundle_sha256", pa.string()),
            ("run_id", pa.string()),
            ("player_account_id", pa.string()),
            ("client_created_at_ms", pa.int64()),
            ("hero", pa.string()),
            ("game_mode", pa.string()),
            ("seed", pa.int64()),
            ("started_at_utc", pa.string()),
            ("ended_at_utc", pa.string()),
            ("status", pa.string()),
            ("run_day", pa.int64()),
            ("run_hour", pa.int64()),
            ("victories", pa.int64()),
            ("losses", pa.int64()),
            ("initial_rank", pa.string()),
            ("initial_rating", pa.int64()),
            ("final_rank", pa.string()),
            ("final_rating", pa.int64()),
            ("final_rating_delta", pa.int64()),
            ("final_health", pa.int64()),
            ("prestige", pa.int64()),
            ("level", pa.int64()),
            ("income", pa.int64()),
            ("gold", pa.int64()),
            ("build_channel", pa.string()),
            ("mod_version", pa.string()),
            ("battle_count", pa.int64()),
            ("replayable_battle_count", pa.int64()),
            ("battle_decided_count", pa.int64()),
            ("battle_player_win_count", pa.int64()),
            ("battle_player_loss_count", pa.int64()),
            ("final_battle_id", pa.string()),
            ("final_player_item_signature", pa.string()),
            ("final_opponent_item_signature", pa.string()),
            ("degradation_categories_json", pa.string()),
            ("degraded_replay_count", pa.int64()),
            ("degraded_event_count", pa.int64()),
            ("degraded_screenshot", pa.bool_()),
            ("projection_code_version", pa.string()),
        ]
    ),
    "battles": pa.schema(
        [
            ("source_hour", pa.string()),
            ("source_day", pa.string()),
            ("available_at_ms", pa.int64()),
            ("bundle_id", pa.string()),
            ("run_id", pa.string()),
            ("battle_id", pa.string()),
            ("recorded_at_utc", pa.string()),
            ("game_day", pa.int64()),
            ("game_hour", pa.int64()),
            ("encounter_id", pa.string()),
            ("combat_kind", pa.string()),
            ("result", pa.string()),
            ("winner_combatant_id", pa.string()),
            ("loser_combatant_id", pa.string()),
            ("is_final_battle", pa.bool_()),
            ("player_account_id", pa.string()),
            ("player_display_name", pa.string()),
            ("player_hero", pa.string()),
            ("player_rank", pa.string()),
            ("player_rating", pa.int64()),
            ("player_level", pa.int64()),
            ("player_prestige", pa.int64()),
            ("player_victories", pa.int64()),
            ("player_income", pa.int64()),
            ("player_gold", pa.int64()),
            ("player_hand_item_count", pa.int64()),
            ("player_skill_count", pa.int64()),
            ("opponent_account_id", pa.string()),
            ("opponent_display_name", pa.string()),
            ("opponent_hero", pa.string()),
            ("opponent_rank", pa.string()),
            ("opponent_rating", pa.int64()),
            ("opponent_level", pa.int64()),
            ("opponent_prestige", pa.int64()),
            ("opponent_victories", pa.int64()),
            ("opponent_income", pa.int64()),
            ("opponent_gold", pa.int64()),
            ("opponent_hand_item_count", pa.int64()),
            ("opponent_skill_count", pa.int64()),
            ("winner_side", pa.string()),
            ("winner_hero", pa.string()),
            ("player_item_signature", pa.string()),
            ("opponent_item_signature", pa.string()),
            ("snapshot_available", pa.bool_()),
            ("replay_available", pa.bool_()),
        ]
    ),
    "battle_cards": pa.schema(
        [
            ("source_hour", pa.string()),
            ("source_day", pa.string()),
            ("available_at_ms", pa.int64()),
            ("bundle_id", pa.string()),
            ("run_id", pa.string()),
            ("battle_id", pa.string()),
            ("card_set_label", pa.string()),
            ("card_set_status", pa.string()),
            ("card_set_source", pa.string()),
            ("owner_side", pa.string()),
            ("card_kind", pa.string()),
            ("slot_index", pa.int64()),
            ("instance_id", pa.string()),
            ("template_id", pa.string()),
            ("card_type", pa.int64()),
            ("size", pa.int64()),
            ("section", pa.int64()),
            ("socket", pa.int64()),
            ("name", pa.string()),
            ("tier", pa.string()),
            ("enchantment", pa.string()),
            ("tags_json", pa.string()),
            ("attributes_json", pa.string()),
        ]
    ),
    "quality": pa.schema(
        [
            ("source_hour", pa.string()),
            ("source_day", pa.string()),
            ("bundle_id", pa.string()),
            ("run_id", pa.string()),
            ("code", pa.string()),
            ("severity", pa.string()),
            ("blocks_release", pa.bool_()),
            ("detail_json", pa.string()),
        ]
    ),
    "quarantine": pa.schema(
        [
            ("source_hour", pa.string()),
            ("source_day", pa.string()),
            ("bundle_id", pa.string()),
            ("run_id", pa.string()),
            ("stage", pa.string()),
            ("reason_code", pa.string()),
            ("raw_run", pa.bool_()),
            ("discarded_unknown_hero", pa.bool_()),
            ("discarded_unknown_final_rank", pa.bool_()),
            ("first_seen_at", pa.string()),
            ("decoder_code_version", pa.string()),
            ("diagnostic_json", pa.string()),
        ]
    ),
}


def table_schemas() -> Mapping[str, pa.Schema]:
    return _SCHEMAS.copy()


def project_hour(index: RawHourIndex, bundles: Iterable[Bundle]) -> HourProjection:
    """Return a one-shot, fixed-size batch stream over the ordered Bundles."""
    return HourProjection(
        index.source_hour,
        index.raw_commit_sha256,
        batch_stream=_ProjectedBatchStream(index, bundles),
    )


class _ProjectedBatchStream:
    __slots__ = ("index", "bundles", "bundle_count")

    def __init__(self, index: RawHourIndex, bundles: Iterable[Bundle]) -> None:
        self.index = index
        self.bundles = bundles
        self.bundle_count: int | None = None

    def __iter__(self) -> Iterator[tuple[str, pa.RecordBatch]]:
        buffers: dict[str, list[dict[str, object]]] = {name: [] for name in _SCHEMAS}
        observed_count = 0
        projected_count = 0
        hour_key = self.index.source_hour.strftime("%Y-%m-%dT%H")
        day_key = self.index.source_hour.strftime("%Y-%m-%d")
        first_seen = (
            (self.index.source_hour + timedelta(hours=1)).isoformat().replace("+00:00", "Z")
        )

        for observed_count, downloaded in enumerate(self.bundles, start=1):
            if (
                observed_count > len(self.index.items)
                or downloaded.ref.bundle_id != self.index.items[observed_count - 1].bundle_id
            ):
                raise ProjectionError("Bundle stream did not match the complete ordered index")
            rows = _project_bundle_rows(
                downloaded,
                hour_key=hour_key,
                day_key=day_key,
                first_seen=first_seen,
            )
            bundle_accounted = 0
            for table_name, row in rows:
                if table_name in ("runs", "quarantine"):
                    bundle_accounted += 1
                buffer = buffers[table_name]
                buffer.append(row)
                if len(buffer) == ROW_BATCH_SIZE:
                    buffers[table_name] = []
                    yield (
                        table_name,
                        pa.RecordBatch.from_pylist(buffer, schema=_SCHEMAS[table_name]),
                    )
            if bundle_accounted != 1:
                raise ProjectionError("Bundle projection accounting is incomplete")
            projected_count += bundle_accounted

        if observed_count != len(self.index.items):
            raise ProjectionError("Bundle stream did not match the complete ordered index")
        if projected_count != len(self.index.items):
            raise ProjectionError("Bundle projection accounting is incomplete")
        for table_name, buffer in buffers.items():
            if buffer:
                yield table_name, pa.RecordBatch.from_pylist(buffer, schema=_SCHEMAS[table_name])
        self.bundle_count = projected_count


def _project_bundle_rows(
    downloaded: Bundle,
    *,
    hour_key: str,
    day_key: str,
    first_seen: str,
) -> Iterator[tuple[str, dict[str, object]]]:
    manifest: Mapping[str, Any] | None = downloaded.manifest
    try:
        decoded = decode_run_payload(downloaded.run_content)
        run_manifest = _object(manifest["run"], "run")
        run_id = _text(run_manifest.get("run_id"), "run.run_id")
        account_id = _text(run_manifest.get("player_account_id"), "run.player_account_id")
        if decoded[1] != run_id or decoded[2] != account_id:
            raise RunPayloadError(
                "payload_identity_mismatch",
                "Run payload and Bundle manifest identities differ",
            )
        manifest_projection = _object(run_manifest.get("projection"), "projection")
        manifest_battles = _array(manifest_projection.get("battles"), "battles")
        manifest_ids = [
            _text(_object(item, "battle").get("battle_id"), "battle_id")
            for item in manifest_battles
        ]
        payload_battles = _array(decoded[5], "payload.battles")
        payload_ids = [_text(_slots(item, 5, "battle")[0], "battle_id") for item in payload_battles]
        payload_id_set = set(payload_ids)
        if len(payload_ids) != len(payload_id_set):
            raise RunPayloadError("duplicate_payload_battle_id", "Run payload repeats a Battle ID")
        if any(item not in payload_id_set for item in manifest_ids):
            raise RunPayloadError(
                "manifest_payload_battle_mismatch",
                "A manifest Battle is absent from the payload",
            )
        run = _slots(decoded[3], 22, "run")
        admission = admit_run(_text(run[0], "run.hero"), _nullable_text(run[12], "run.final_rank"))
        if not admission.accepted:
            yield (
                "quarantine",
                _quarantine_row(
                    hour_key,
                    day_key,
                    downloaded.ref.bundle_id,
                    run_id,
                    "fact_filter",
                    "unaccepted_run",
                    first_seen,
                    raw_run=True,
                    discarded_unknown_hero=admission.unknown_hero,
                    discarded_unknown_final_rank=admission.unknown_final_rank,
                    detail={"hero": admission.hero, "final_rank": admission.final_rank},
                ),
            )
            return
        projected = _prepare_valid_bundle(
            downloaded,
            manifest,
            decoded,
            admission,
            hour_key=hour_key,
            day_key=day_key,
        )
    except (
        RunPayloadError,
        KeyError,
        TypeError,
        ValueError,
    ) as error:
        reason = error.reason if isinstance(error, RunPayloadError) else "run_payload_decode_failed"
        run_id = None
        try:
            if manifest is not None:
                run_id = _object(manifest["run"], "run").get("run_id")
        except KeyError, TypeError, ValueError:
            pass
        yield (
            "quarantine",
            _quarantine_row(
                hour_key,
                day_key,
                downloaded.ref.bundle_id,
                run_id if isinstance(run_id, str) else None,
                "run_payload_decode",
                reason,
                first_seen,
            ),
        )
        return
    yield from projected.rows()


@dataclass(frozen=True, slots=True)
class _BattleSummary:
    wins: int
    losses: int
    heroes: frozenset[str]
    ranks: frozenset[str]
    recorded_in_future: bool
    recorded_before_run: bool


@dataclass(frozen=True, slots=True)
class _ValidBundleProjection:
    base: Mapping[str, object]
    run: Mapping[str, object]
    battles: Sequence[tuple[Any, ...]]
    replayable_ids: Sequence[str]
    quality: Sequence[dict[str, object]]

    def rows(self) -> Iterator[tuple[str, dict[str, object]]]:
        yield "runs", dict(self.run)
        for battle in self.battles:
            yield "battles", _battle_row(self.base, battle, self.replayable_ids)
            for card in _card_rows(self.base, battle):
                yield "battle_cards", card
        for row in self.quality:
            yield "quality", row


def _prepare_valid_bundle(
    downloaded: Bundle,
    manifest: Mapping[str, Any],
    payload: tuple[Any, ...],
    admission: RunAdmission,
    *,
    hour_key: str,
    day_key: str,
) -> _ValidBundleProjection:
    run_id = _text(payload[1], "run_id")
    account_id = _text(payload[2], "player_account_id")
    run = _slots(payload[3], 22, "run")
    battles = [_slots(item, 5, "battle") for item in _array(payload[5], "battles")]
    replayable_ids = [_text(item, "replayable_id") for item in _array(payload[6], "replayable_ids")]
    degradation = _slots(payload[7], 4, "degradation")
    available_at_ms = downloaded.ref.available_at_ms
    base = {
        "source_hour": hour_key,
        "source_day": day_key,
        "available_at_ms": available_at_ms,
        "bundle_id": downloaded.ref.bundle_id,
        "run_id": run_id,
    }

    wins = 0
    losses = 0
    heroes: set[str] = set()
    ranks: set[str] = set()
    recorded_in_future = False
    recorded_before_run = False
    available = datetime.fromtimestamp(available_at_ms / 1_000, tz=UTC)
    started = _parse_time(_text(run[3], "run.started_at"))
    for battle in battles:
        battle_row = _battle_row(base, battle, replayable_ids)
        wins += battle_row["winner_side"] == "player"
        losses += battle_row["winner_side"] == "opponent"
        heroes.update(
            value
            for value in (
                battle_row["player_hero"],
                battle_row["opponent_hero"],
            )
            if isinstance(value, str)
        )
        ranks.update(
            value
            for value in (
                battle_row["player_rank"],
                battle_row["opponent_rank"],
            )
            if isinstance(value, str)
        )
        recorded = _parse_time(battle_row["recorded_at_utc"])
        recorded_in_future |= recorded is not None and recorded > available
        recorded_before_run |= recorded is not None and started is not None and recorded < started
        for _card in _card_rows(base, battle):
            pass
    summary = _BattleSummary(
        wins,
        losses,
        frozenset(heroes),
        frozenset(ranks),
        recorded_in_future,
        recorded_before_run,
    )
    finals = [
        battle
        for battle in battles
        if _boolean(_slots(battle[1], 9, "battle.facts")[8], "is_final")
    ]
    final = finals[0] if len(finals) == 1 else None
    run_row = {
        **base,
        "bundle_sha256": downloaded.sha256,
        "player_account_id": account_id,
        "client_created_at_ms": _integer(manifest.get("created_at_ms"), "created_at_ms"),
        "hero": admission.hero,
        "game_mode": _text(run[1], "run.game_mode").strip(),
        "seed": _nullable_integer(run[2], "run.seed"),
        "started_at_utc": _text(run[3], "run.started_at"),
        "ended_at_utc": _nullable_text(run[4], "run.ended_at"),
        "status": _text(run[5], "run.status"),
        "run_day": _nullable_integer(run[6], "run.day"),
        "run_hour": _nullable_integer(run[7], "run.hour"),
        "victories": _nullable_integer(run[8], "run.victories"),
        "losses": _nullable_integer(run[9], "run.losses"),
        "initial_rank": _nullable_text(run[10], "run.initial_rank"),
        "initial_rating": _nullable_integer(run[11], "run.initial_rating"),
        "final_rank": admission.final_rank,
        "final_rating": _nullable_integer(run[13], "run.final_rating"),
        "final_rating_delta": _nullable_integer(run[14], "run.final_rating_delta"),
        "final_health": _nullable_integer(run[15], "run.final_health"),
        "prestige": _nullable_integer(run[16], "run.prestige"),
        "level": _nullable_integer(run[17], "run.level"),
        "income": _nullable_integer(run[18], "run.income"),
        "gold": _nullable_integer(run[19], "run.gold"),
        "build_channel": _nullable_text(run[20], "run.build_channel"),
        "mod_version": _text(run[21], "run.mod_version"),
        "battle_count": len(battles),
        "replayable_battle_count": len(replayable_ids),
        "battle_decided_count": wins + losses,
        "battle_player_win_count": wins,
        "battle_player_loss_count": losses,
        "final_battle_id": _text(final[0], "battle_id") if final else None,
        "final_player_item_signature": _card_signature(final, "player_hand"),
        "final_opponent_item_signature": _card_signature(final, "opponent_hand"),
        "degradation_categories_json": _json(_array(degradation[0], "degradation.categories")),
        "degraded_replay_count": len(_array(degradation[1], "degradation.replays")),
        "degraded_event_count": _integer(degradation[2], "degradation.events"),
        "degraded_screenshot": _boolean(degradation[3], "degradation.screenshot"),
        "projection_code_version": PROJECTION_VERSION,
    }
    quality = _quality_rows(
        base,
        run_row,
        summary,
        battles,
        replayable_ids,
        degradation,
    )
    return _ValidBundleProjection(base, run_row, battles, replayable_ids, quality)


def _battle_row(
    base: Mapping[str, object], battle: tuple[Any, ...], replayable_ids: Sequence[str]
) -> dict[str, object]:
    battle_id = _text(battle[0], "battle_id")
    facts = _slots(battle[1], 9, "battle.facts")
    participants = _slots(battle[2], 2, "battle.participants")
    player = _slots(participants[0], 12, "battle.player")
    opponent = _slots(participants[1], 12, "battle.opponent")
    player_id = _nullable_text(player[0], "player.account_id")
    opponent_id = _nullable_text(opponent[0], "opponent.account_id")
    winner_id = _nullable_text(facts[6], "battle.winner_id")
    if winner_id == "Player" or (winner_id is not None and winner_id == player_id):
        winner_side = "player"
        winner_hero = normalize_hero(_nullable_text(player[2], "player.hero"))
    elif winner_id == "Opponent" or (winner_id is not None and winner_id == opponent_id):
        winner_side = "opponent"
        winner_hero = normalize_hero(_nullable_text(opponent[2], "opponent.hero"))
    else:
        winner_side = None
        winner_hero = None
    return {
        **base,
        "battle_id": battle_id,
        "recorded_at_utc": _text(facts[0], "battle.recorded_at"),
        "game_day": _nullable_integer(facts[1], "battle.day"),
        "game_hour": _nullable_integer(facts[2], "battle.hour"),
        "encounter_id": _nullable_text(facts[3], "battle.encounter_id"),
        "combat_kind": _text(facts[4], "battle.combat_kind"),
        "result": _nullable_text(facts[5], "battle.result"),
        "winner_combatant_id": winner_id,
        "loser_combatant_id": _nullable_text(facts[7], "battle.loser_id"),
        "is_final_battle": _boolean(facts[8], "battle.is_final"),
        **_participant("player", player),
        **_participant("opponent", opponent),
        "winner_side": winner_side,
        "winner_hero": winner_hero,
        "player_item_signature": _card_signature(battle, "player_hand"),
        "opponent_item_signature": _card_signature(battle, "opponent_hand"),
        "snapshot_available": battle[3] is not None,
        "replay_available": _replay_available(battle, replayable_ids),
    }


def _participant(prefix: str, participant: tuple[Any, ...]) -> dict[str, object]:
    return {
        f"{prefix}_account_id": _nullable_text(participant[0], f"{prefix}.account_id"),
        f"{prefix}_display_name": _nullable_text(participant[1], f"{prefix}.display_name"),
        f"{prefix}_hero": normalize_hero(_nullable_text(participant[2], f"{prefix}.hero")),
        f"{prefix}_rank": _nullable_text(participant[3], f"{prefix}.rank"),
        f"{prefix}_rating": _nullable_integer(participant[4], f"{prefix}.rating"),
        f"{prefix}_level": _nullable_integer(participant[5], f"{prefix}.level"),
        f"{prefix}_prestige": _nullable_integer(participant[6], f"{prefix}.prestige"),
        f"{prefix}_victories": _nullable_integer(participant[7], f"{prefix}.victories"),
        f"{prefix}_income": _nullable_integer(participant[8], f"{prefix}.income"),
        f"{prefix}_gold": _nullable_integer(participant[9], f"{prefix}.gold"),
        f"{prefix}_hand_item_count": _nullable_integer(participant[10], f"{prefix}.hand_count"),
        f"{prefix}_skill_count": _nullable_integer(participant[11], f"{prefix}.skill_count"),
    }


def _card_rows(base: Mapping[str, object], battle: tuple[Any, ...]) -> Iterator[dict[str, object]]:
    if battle[3] is None:
        return
    snapshots = _slots(battle[3], 1, "battle.snapshots")
    for raw_set in _array(snapshots[0], "battle.card_sets"):
        card_set = _slots(raw_set, 4, "battle.card_set")
        label = _text(card_set[0], "card_set.label")
        owner = (
            "player"
            if label.startswith("player_")
            else "opponent"
            if label.startswith("opponent_")
            else "unknown"
        )
        kind = (
            "item"
            if label.endswith("_hand")
            else "skill"
            if label.endswith("_skills")
            else "unknown"
        )
        for slot_index, raw_card in enumerate(_array(card_set[3], "card_set.cards")):
            card = _slots(raw_card, 11, "battle.card")
            attributes = _object(card[10], "card.attributes")
            yield {
                **base,
                "battle_id": _text(battle[0], "battle_id"),
                "card_set_label": label,
                "card_set_status": _nullable_text(card_set[1], "card_set.status"),
                "card_set_source": _nullable_text(card_set[2], "card_set.source"),
                "owner_side": owner,
                "card_kind": kind,
                "slot_index": slot_index,
                "instance_id": _text(card[0], "card.instance_id"),
                "template_id": _text(card[1], "card.template_id"),
                "card_type": _integer(card[2], "card.type"),
                "size": _integer(card[3], "card.size"),
                "section": _nullable_integer(card[4], "card.section"),
                "socket": _nullable_integer(card[5], "card.socket"),
                "name": _nullable_text(card[6], "card.name"),
                "tier": _nullable_text(card[7], "card.tier"),
                "enchantment": _nullable_text(card[8], "card.enchantment"),
                "tags_json": _json(_array(card[9], "card.tags")),
                "attributes_json": _json(attributes),
            }


def _card_sets(battle: tuple[Any, ...] | None) -> dict[str, tuple[Any, ...]]:
    if battle is None or battle[3] is None:
        return {}
    snapshots = _slots(battle[3], 1, "battle.snapshots")
    result: dict[str, tuple[Any, ...]] = {}
    duplicates: set[str] = set()
    for raw in _array(snapshots[0], "battle.card_sets"):
        value = _slots(raw, 4, "battle.card_set")
        label = _text(value[0], "card_set.label")
        if label in result:
            duplicates.add(label)
        result[label] = value
    for label in duplicates:
        result.pop(label, None)
    return result


def _card_signature(battle: tuple[Any, ...] | None, label: str) -> str | None:
    card_set = _card_sets(battle).get(label)
    if card_set is None or (_nullable_text(card_set[1], "status") or "").lower() == "missing":
        return None
    templates = [
        _text(_slots(card, 11, "card")[1], "card.template_id")
        for card in _array(card_set[3], "cards")
    ]
    if not templates:
        return None
    return hashlib.sha256(_json(templates).encode()).hexdigest()


def _replay_available(battle: tuple[Any, ...], replayable_ids: Sequence[str]) -> bool:
    battle_id = _text(battle[0], "battle_id")
    if battle_id not in replayable_ids or battle[3] is None or battle[4] is None:
        return False
    replay = _slots(battle[4], 4, "battle.replay")
    if not all(isinstance(value, bytes) and value for value in replay[1:]):
        return False
    sets = _card_sets(battle)
    required = {"player_hand", "player_skills", "opponent_hand", "opponent_skills"}
    return set(sets) == required and all(
        (_nullable_text(value[1], "card_set.status") or "").lower() != "missing"
        for value in sets.values()
    )


def _quality_rows(
    base: Mapping[str, object],
    run: Mapping[str, object],
    battle_summary: _BattleSummary,
    battles: Sequence[tuple[Any, ...]],
    replayable_ids: Sequence[str],
    degradation: tuple[Any, ...],
) -> list[dict[str, object]]:
    findings: dict[str, dict[str, object]] = {}
    payload_ids = {_text(battle[0], "battle_id") for battle in battles}
    if not set(replayable_ids).issubset(payload_ids):
        findings["run_battle_count_mismatch"] = {}
    wins = battle_summary.wins
    losses = battle_summary.losses
    if (
        run["victories"] is not None
        and run["losses"] is not None
        and (run["victories"] != wins or run["losses"] != losses)
    ):
        findings["run_outcome_count_mismatch"] = {
            "run_victories": run["victories"],
            "run_losses": run["losses"],
            "battle_wins": wins,
            "battle_losses": losses,
        }
    finals = [battle for battle in battles if _slots(battle[1], 9, "facts")[8] is True]
    if not finals:
        findings["final_battle_missing"] = {}
    elif len(finals) > 1:
        findings["multiple_final_battles"] = {"count": len(finals)}
    elif _card_signature(finals[0], "player_hand") is None:
        findings["final_player_hand_missing"] = {}
    heroes = {run["hero"]} | set(battle_summary.heroes)
    unknown_heroes = sorted(str(value) for value in heroes if value not in CANONICAL_HEROES)
    if unknown_heroes:
        findings["unknown_hero"] = {"values": unknown_heroes}
    ranks = {run["initial_rank"], run["final_rank"]} | set(battle_summary.ranks)
    unknown_ranks = sorted(
        str(value) for value in ranks if value is not None and value not in CANONICAL_RANKS
    )
    if unknown_ranks:
        findings["unknown_rank"] = {"values": unknown_ranks}
    available = datetime.fromtimestamp(
        _integer(base["available_at_ms"], "available_at_ms") / 1_000,
        tz=UTC,
    )
    observed = [
        datetime.fromtimestamp(
            _integer(run["client_created_at_ms"], "client_created_at_ms") / 1_000,
            tz=UTC,
        ),
        *filter(
            None,
            [_parse_time(run["started_at_utc"]), _parse_time(run["ended_at_utc"])],
        ),
    ]
    if battle_summary.recorded_in_future or any(value > available for value in observed):
        findings["client_clock_future"] = {}
    if battle_summary.recorded_before_run:
        findings["client_clock_before_run"] = {}
    if _array(degradation[1], "degradation.replays"):
        findings["degraded_replay"] = {}
    if _integer(degradation[2], "degradation.events"):
        findings["degraded_events"] = {}
    if _boolean(degradation[3], "degradation.screenshot"):
        findings["degraded_screenshot"] = {}
    return [
        {
            "source_hour": base["source_hour"],
            "source_day": base["source_day"],
            "bundle_id": base["bundle_id"],
            "run_id": base["run_id"],
            "code": code,
            "severity": "WARN",
            "blocks_release": False,
            "detail_json": _json(detail),
        }
        for code, detail in sorted(findings.items())
    ]


def _quarantine_row(
    hour: str,
    day: str,
    bundle_id: str,
    run_id: str | None,
    stage: str,
    reason: str,
    first_seen: str,
    *,
    raw_run: bool = False,
    discarded_unknown_hero: bool = False,
    discarded_unknown_final_rank: bool = False,
    detail: Mapping[str, object] | None = None,
) -> dict[str, object]:
    return {
        "source_hour": hour,
        "source_day": day,
        "bundle_id": bundle_id,
        "run_id": run_id,
        "stage": stage,
        "reason_code": reason,
        "raw_run": raw_run,
        "discarded_unknown_hero": discarded_unknown_hero,
        "discarded_unknown_final_rank": discarded_unknown_final_rank,
        "first_seen_at": first_seen,
        "decoder_code_version": PROJECTION_VERSION,
        "diagnostic_json": _json({"reason": reason, **(detail or {})}),
    }


def decode_run_payload(content: bytes) -> tuple[Any, ...]:
    if len(content) < 2 or content[:2] != b"\x1f\x8b":
        raise RunPayloadError("run_payload_not_gzip", "Run payload is not gzip")
    output = bytearray()
    try:
        with gzip.GzipFile(fileobj=BytesIO(content), mode="rb") as stream:
            while True:
                chunk = stream.read(min(64 * 1024, MAX_DECOMPRESSED_RUN_BYTES - len(output) + 1))
                if not chunk:
                    break
                output.extend(chunk)
                if len(output) > MAX_DECOMPRESSED_RUN_BYTES:
                    raise RunPayloadError(
                        "run_payload_decompressed_too_large", "Run payload is too large"
                    )
    except RunPayloadError:
        raise
    except (EOFError, OSError) as error:
        raise RunPayloadError("run_payload_decode_failed", "Run gzip is invalid") from error
    try:
        decoded = _MessagePack(bytes(output)).unpack()
        root = _slots(decoded, 8, "payload")
    except (ValueError, TypeError, struct.error) as error:
        raise RunPayloadError("run_payload_decode_failed", "Run MessagePack is invalid") from error
    if _integer(root[0], "payload.version") != 5:
        raise RunPayloadError("payload_version_unsupported", "Run version is unsupported")
    return root


class _MessagePack:
    """Small strict decoder for the scalar/array/map subset used by RunPayload V5."""

    def __init__(self, content: bytes) -> None:
        self._content = content
        self._offset = 0

    def unpack(self) -> object:
        value = self._one()
        if self._offset != len(self._content):
            raise ValueError("trailing MessagePack bytes")
        return value

    def _take(self, count: int) -> bytes:
        end = self._offset + count
        if end > len(self._content):
            raise ValueError("truncated MessagePack")
        value = self._content[self._offset : end]
        self._offset = end
        return value

    def _number(self, fmt: str) -> int | float:
        return cast(int | float, struct.unpack(fmt, self._take(struct.calcsize(fmt)))[0])

    def _one(self) -> object:
        marker = self._take(1)[0]
        if marker <= 0x7F:
            return marker
        if marker >= 0xE0:
            return marker - 256
        if 0xA0 <= marker <= 0xBF:
            return self._take(marker & 0x1F).decode("utf-8")
        if 0x90 <= marker <= 0x9F:
            return [self._one() for _ in range(marker & 0x0F)]
        if 0x80 <= marker <= 0x8F:
            return {self._one(): self._one() for _ in range(marker & 0x0F)}
        if marker == 0xC0:
            return None
        if marker in {0xC2, 0xC3}:
            return marker == 0xC3
        if marker in {0xC4, 0xC5, 0xC6}:
            size = int(self._number({0xC4: ">B", 0xC5: ">H", 0xC6: ">I"}[marker]))
            return self._take(size)
        if marker in {0xCA, 0xCB}:
            return self._number(">f" if marker == 0xCA else ">d")
        if marker in {0xCC, 0xCD, 0xCE, 0xCF}:
            return self._number({0xCC: ">B", 0xCD: ">H", 0xCE: ">I", 0xCF: ">Q"}[marker])
        if marker in {0xD0, 0xD1, 0xD2, 0xD3}:
            return self._number({0xD0: ">b", 0xD1: ">h", 0xD2: ">i", 0xD3: ">q"}[marker])
        if marker in {0xD9, 0xDA, 0xDB}:
            size = int(self._number({0xD9: ">B", 0xDA: ">H", 0xDB: ">I"}[marker]))
            return self._take(size).decode("utf-8")
        if marker in {0xDC, 0xDD}:
            size = int(self._number(">H" if marker == 0xDC else ">I"))
            return [self._one() for _ in range(size)]
        if marker in {0xDE, 0xDF}:
            size = int(self._number(">H" if marker == 0xDE else ">I"))
            return {self._one(): self._one() for _ in range(size)}
        raise ValueError(f"unsupported MessagePack marker: {marker:#x}")


def _slots(value: object, count: int, field: str) -> tuple[Any, ...]:
    if isinstance(value, (list, tuple)) and len(value) >= count:
        return tuple(value)
    if isinstance(value, dict):
        try:
            return tuple(
                value[index] if index in value else value[str(index)] for index in range(count)
            )
        except KeyError as error:
            raise ValueError(f"{field} omits a numeric field") from error
    raise ValueError(f"{field} must be a numeric-key array or map")


def _array(value: object, field: str) -> list[Any]:
    if not isinstance(value, (list, tuple)):
        raise ValueError(f"{field} must be an array")
    return list(value)


def _object(value: object, field: str) -> Mapping[Any, Any]:
    if not isinstance(value, dict):
        raise ValueError(f"{field} must be an object")
    return value


def _text(value: object, field: str) -> str:
    if not isinstance(value, str):
        raise ValueError(f"{field} must be a string")
    return value


def _nullable_text(value: object, field: str) -> str | None:
    return None if value is None else _text(value, field)


def _integer(value: object, field: str) -> int:
    if not isinstance(value, int) or isinstance(value, bool):
        raise ValueError(f"{field} must be an integer")
    return value


def _nullable_integer(value: object, field: str) -> int | None:
    return None if value is None else _integer(value, field)


def _boolean(value: object, field: str) -> bool:
    if not isinstance(value, bool):
        raise ValueError(f"{field} must be boolean")
    return value


def _parse_time(value: object) -> datetime | None:
    if not isinstance(value, str):
        return None
    try:
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError:
        return None
    if parsed.tzinfo is None or parsed.utcoffset() is None:
        return None
    return parsed.astimezone(UTC)


def _json(value: object) -> str:
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False)
