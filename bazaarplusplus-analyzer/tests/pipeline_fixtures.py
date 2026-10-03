"""Bundles, a Bundle Server stand-in, and golden files for end-to-end pipeline tests.

Every Bundle here reaches the analyzer the way production Bundles do: listed by
``GET /bundles``, downloaded, admitted, projected, committed, and sealed.
"""

import base64
import difflib
import hashlib
import json
import os
import shutil
from collections.abc import Callable, Iterable
from dataclasses import dataclass
from datetime import UTC, date, datetime, time, timedelta
from pathlib import Path

import httpx

from bppanalyzer.bundle_source import BundleSource
from bppanalyzer.driver import PipelineDriver
from bppanalyzer.fact_store import FactStore
from tests.bundle_fixtures import Card, bundle_bytes, envelope, payload

REPO_ROOT = Path(__file__).resolve().parents[2]
SERVER_FIXTURES = REPO_ROOT / "bazaarplusplus-server/contracts/v5/fixtures"
MOD_RUN_FIXTURE = (
    REPO_ROOT / "bazaarplusplus-mod/tests/BundleV5Codec.Tests/fixtures/run-payload-v5.fixture.b64"
)
CONTRACT_FIXTURES = REPO_ROOT / "bazaarplusplus-analyzer/contracts/v5/fixtures"
TEST_FIXTURES = Path(__file__).parent / "fixtures"
PROJECTION_GOLDENS = TEST_FIXTURES / "projection"

API_BASE_URL = "https://api.invalid"
DOWNLOAD_BASE_URL = "https://download.invalid"
SYNC_TOKEN = "test-token"

MOD_BUNDLE_ID = "01J00000000000000000000902"

# The single Source Day of the golden pipeline run.
GOLDEN_DAY = date(2026, 8, 10)

# Real boards hold small (1), medium (2), and large (3) cards; the mod rejects
# any other size.
DOOLEY_CARDS = (
    "11111111-1111-4111-8111-111111111111",
    "22222222-2222-4222-8222-222222222222",
    "33333333-3333-4333-8333-333333333333",
    "44444444-4444-4444-8444-444444444444",
)
JULES_CARDS = (
    "55555555-5555-4555-8555-555555555555",
    "66666666-6666-4666-8666-666666666666",
    "77777777-7777-4777-8777-777777777777",
    "88888888-8888-4888-8888-888888888888",
)
FULL_BOARD = (3, 3, 2, 2)
SOCKET_EFFECT_CARD = "eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee"


def _ms(value: datetime) -> int:
    return int(value.timestamp() * 1_000)


def _hour(day: date, hour: int) -> datetime:
    return datetime.combine(day, time(hour), UTC)


def fixed_clock(now: datetime) -> Callable[[], datetime]:
    return lambda: now


def read_base64(path: Path) -> bytes:
    return base64.b64decode(path.read_text(encoding="ascii").strip(), validate=True)


@dataclass(frozen=True, slots=True)
class ServedBundle:
    """One Bundle as the Bundle Server lists and serves it."""

    bundle_id: str
    available_at_ms: int
    content: bytes


def server_run_only_bundle(available_at_ms: int) -> ServedBundle:
    """The server's run-only golden; its Run segment never decodes."""
    manifest = json.loads((SERVER_FIXTURES / "run-only.manifest.json").read_text())
    content = read_base64(SERVER_FIXTURES / "run-only.bundle.b64")
    return ServedBundle(manifest["bundle_id"], available_at_ms, content)


def mod_run_payload_bundle(available_at_ms: int, *, created_at_ms: int) -> ServedBundle:
    """Envelope the mod-encoded Run golden; the C# writer owns the Run bytes."""
    run_content = read_base64(MOD_RUN_FIXTURE)
    manifest: dict[str, object] = {
        "bundle_id": MOD_BUNDLE_ID,
        "bundle_version": 5,
        "created_at_ms": created_at_ms,
        "run": {
            "run_id": "payload-run",
            "player_account_id": "golden-account",
            "run_format_version": 5,
            "projection": {"run": {}, "battles": []},
            "payload": {
                "offset": 0,
                "length": len(run_content),
                "sha256": hashlib.sha256(run_content).hexdigest(),
                "content_type": "application/x-bpp-run-v5",
            },
        },
    }
    return ServedBundle(
        MOD_BUNDLE_ID, available_at_ms, envelope(manifest, run_content, sort_keys=True)
    )


def _synthetic(
    bundle_id: str, available_at_ms: int, *, created_at_ms: int | None = None, **run: object
) -> ServedBundle:
    run_id = str(run.setdefault("run_id", f"run-{bundle_id}"))
    account_id = str(run.setdefault("account_id", f"account-{bundle_id}"))
    content = bundle_bytes(
        bundle_id,
        run_payload=payload(**run),  # type: ignore[arg-type]
        created_at_ms=created_at_ms,
        run_id=run_id,
        account_id=account_id,
    )
    return ServedBundle(bundle_id, available_at_ms, content)


# One Bundle per projection rule. Each is projected alone by
# test_bundle_goldens and also served inside the golden pipeline day.
_PROJECTION_HOUR = _hour(GOLDEN_DAY, 13)
_PROJECTION_RUNS: dict[str, dict[str, object]] = {
    "side-name-player-wins": {
        "winner_combatant_id": "Player",
        "loser_combatant_id": "Opponent",
        "victories": 1,
        "losses": 0,
    },
    "side-name-opponent-wins": {
        "winner_combatant_id": "Opponent",
        "loser_combatant_id": "Player",
        "victories": 0,
        "losses": 1,
        "final_rank": "Gold",
    },
    "account-id-player-wins": {
        "account_id": "account-1",
        "winner_combatant_id": "account-1",
        "loser_combatant_id": "account-2",
    },
    "account-id-opponent-wins": {
        "account_id": "account-1",
        "winner_combatant_id": "account-2",
        "loser_combatant_id": "account-1",
        "victories": 10,
        "losses": 3,
    },
    "unknown-combatant-undecided": {
        "winner_combatant_id": "unknown-combatant",
        "loser_combatant_id": "another-unknown-combatant",
        "victories": 5,
        "losses": 5,
    },
    "client-clock-anomalies": {
        "started_at": "2026-08-11T01:00:00Z",
        "battle_at": "2026-08-09T23:00:00Z",
        "created_at_ms": _ms(datetime(2026, 8, 11, tzinfo=UTC)),
    },
    "unaccepted-unknown-hero": {"hero": "UnknownHero", "final_rank": "Legendary"},
    "unaccepted-missing-rank": {"hero": "Vanessa", "final_rank": None},
    "unaccepted-unknown-rank": {"hero": "Vanessa", "final_rank": "Mythic"},
    "unaccepted-unknown-hero-and-rank": {"hero": "UnknownHero", "final_rank": "Mythic"},
    "hero8-alias": {"hero": "Hero8"},
    "whitespace-hero": {"hero": "  Vanessa  "},
}
PROJECTION_CASES: dict[str, ServedBundle] = {
    name: _synthetic(
        f"projection-{name}",
        _ms(_PROJECTION_HOUR) + (number + 1) * 1_000,
        **run,
    )
    for number, (name, run) in enumerate(_PROJECTION_RUNS.items())
}


def _layout(
    number: int,
    hero: str,
    card_ids: tuple[str, ...],
    sizes: tuple[int, ...],
    *,
    slots: tuple[int, ...] | None = None,
    tier: str = "Gold",
    enchantments: tuple[str | None, ...] | None = None,
    socket_effect_slots: tuple[int, ...] = (),
    **run: object,
) -> ServedBundle:
    if slots is None:
        tiled = []
        cursor = 0
        for size in sizes:
            tiled.append(cursor)
            cursor += size
        slots = tuple(tiled)
    named = enchantments or (None,) * len(card_ids)
    hand = [
        Card(card_id, size, slot, tier, enchantment)
        for card_id, size, slot, enchantment in zip(card_ids, sizes, slots, named, strict=True)
    ]
    hand.extend(
        Card(SOCKET_EFFECT_CARD, 1, slot, tier, card_type=7, name="[Cooler] Socket Effect")
        for slot in socket_effect_slots
    )
    return _synthetic(
        f"layout-{number:02d}",
        _ms(_hour(GOLDEN_DAY, 14)) + (number + 1) * 1_000,
        hero=hero,
        player_hand=hand,
        **run,
    )


_DOOLEY_ENCHANTMENTS = (None, "Burning", None, None)

# Build eligibility boundaries. Numbering follows the retired row-level
# policy fixture; number 2 (a final_battle_id that disagrees with the final
# Battle) cannot be expressed as a Bundle because projection derives
# final_battle_id from the one Battle flagged final.
LAYOUT_CASES: dict[str, ServedBundle] = {
    "valid": _layout(0, "Dooley", DOOLEY_CARDS, FULL_BOARD, enchantments=_DOOLEY_ENCHANTMENTS),
    "two-final-battles": _layout(1, "Dooley", DOOLEY_CARDS, FULL_BOARD, final_battles=2),
    "missing-final-hand": _layout(
        3, "Dooley", DOOLEY_CARDS, FULL_BOARD, player_hand_status="Missing"
    ),
    "non-uuid-card": _layout(4, "Dooley", ("not-a-card-id", *DOOLEY_CARDS[1:]), FULL_BOARD),
    "zero-size-card": _layout(
        5, "Dooley", (*DOOLEY_CARDS, JULES_CARDS[0]), (0, *FULL_BOARD), slots=(0, 0, 3, 6, 8)
    ),
    "nine-slots": _layout(6, "Dooley", DOOLEY_CARDS, (3, 3, 2, 1)),
    "overlapping-slots": _layout(7, "Dooley", DOOLEY_CARDS, FULL_BOARD, slots=(0, 2, 6, 8)),
    "overflowing-slots": _layout(8, "Dooley", DOOLEY_CARDS, FULL_BOARD, slots=(0, 3, 6, 9)),
    "socket-effects-ignored": _layout(
        9,
        "Dooley",
        DOOLEY_CARDS,
        FULL_BOARD,
        enchantments=_DOOLEY_ENCHANTMENTS,
        socket_effect_slots=(0, 3),
    ),
    "jules-gold-day-1": _layout(10, "Jules", JULES_CARDS, FULL_BOARD, run_day=1),
    "jules-gold-day-2": _layout(11, "Jules", JULES_CARDS, FULL_BOARD, run_day=2),
    "jules-diamond-day-3": _layout(12, "Jules", JULES_CARDS, FULL_BOARD, run_day=3, tier="Diamond"),
    "jules-diamond-day-100": _layout(
        13, "Jules", JULES_CARDS, FULL_BOARD, run_day=100, tier="Diamond"
    ),
    "jules-eight-two": _layout(
        14, "Jules", JULES_CARDS, FULL_BOARD, victories=8, losses=2, run_day=8
    ),
}


def golden_day_bundles() -> tuple[ServedBundle, ...]:
    """Every Bundle of the golden Source Day; all other hours list no Bundles."""
    return (
        server_run_only_bundle(_ms(_hour(GOLDEN_DAY, 1))),
        mod_run_payload_bundle(_ms(_hour(GOLDEN_DAY, 2)), created_at_ms=_ms(_hour(GOLDEN_DAY, 2))),
        *PROJECTION_CASES.values(),
        *LAYOUT_CASES.values(),
    )


def daily_bundles(start: date, days: int) -> tuple[ServedBundle, ...]:
    """One accepted Dooley Run at 00:30 on each Source Day."""
    bundles = []
    for offset in range(days):
        day = start + timedelta(days=offset)
        midnight = _hour(day, 0)

        def stamp(minutes: int, at: datetime = midnight) -> str:
            return (at + timedelta(minutes=minutes)).isoformat().replace("+00:00", "Z")

        bundles.append(
            _synthetic(
                f"daily-{day.isoformat()}",
                _ms(midnight + timedelta(minutes=30)),
                created_at_ms=_ms(midnight + timedelta(minutes=25)),
                hero="Dooley",
                started_at=stamp(0),
                battle_at=stamp(10),
                ended_at=stamp(20),
            )
        )
    return tuple(bundles)


class BundleServer:
    """``httpx.MockTransport`` handler for the Bundle Server collection contract.

    ``GET /bundles`` returns the four-field items of
    ``bazaarplusplus-server/src/modules/bundle-collection.ts::BundleCollectionItem``
    in keyset order on a single page. An hour without Bundles returns ``200`` with
    no items. ``GET <download_url>`` returns the Bundle bytes.
    """

    def __init__(self, bundles: Iterable[ServedBundle], *, now: datetime) -> None:
        self.bundles = tuple(
            sorted(bundles, key=lambda item: (item.available_at_ms, item.bundle_id))
        )
        self.by_id = {bundle.bundle_id: bundle for bundle in self.bundles}
        if len(self.by_id) != len(self.bundles):
            raise ValueError("Served Bundle IDs must be unique")
        self.download_expires_at_ms = _ms(now + timedelta(hours=1))

    def __call__(self, request: httpx.Request) -> httpx.Response:
        if request.url.host == "api.invalid" and request.url.path == "/bundles":
            start = int(request.url.params["available_from_ms"])
            end = int(request.url.params["available_before_ms"])
            items = [
                {
                    "bundle_id": bundle.bundle_id,
                    "available_at_ms": bundle.available_at_ms,
                    "download_url": f"{DOWNLOAD_BASE_URL}/{bundle.bundle_id}",
                    "download_expires_at_ms": self.download_expires_at_ms,
                }
                for bundle in self.bundles
                if start <= bundle.available_at_ms < end
            ]
            return httpx.Response(
                200,
                json={
                    "window": {"available_from_ms": start, "available_before_ms": end},
                    "items": items,
                    "next_after": None,
                },
            )
        if request.url.host == "download.invalid":
            bundle = self.by_id.get(request.url.path.removeprefix("/"))
            if bundle is not None:
                return httpx.Response(200, content=bundle.content)
        return httpx.Response(404)


def heal_from_bundles(root: Path, days: int, *, start: date = date(2026, 8, 7)) -> FactStore:
    """Seal ``days`` consecutive Source Days from :func:`daily_bundles`, one day per run.

    Each day is healed by its own ``PipelineDriver`` run at 00:01 the next day,
    so every listing stays inside the Bundle Source's eight-day retention. Only
    ``facts/`` is kept, so callers start from sealed facts without the healing
    runs' status, logs, or local snapshots.
    """
    bundles = daily_bundles(start, days)
    for offset in range(days):
        source_day = start + timedelta(days=offset)
        now = _hour(source_day + timedelta(days=1), 0) + timedelta(minutes=1)
        server = BundleServer(bundles, now=now)
        with httpx.Client(transport=httpx.MockTransport(server)) as client:
            summary = PipelineDriver(
                root,
                source=BundleSource(
                    api_base_url=API_BASE_URL,
                    sync_token=SYNC_TOKEN,
                    client=client,
                    clock=fixed_clock(now),
                ),
                clock=fixed_clock(now),
                duckdb_memory_limit="1GB",
                duckdb_threads=1,
            ).run(heal_days=2, publish=False)
        if summary.failures or not FactStore(root).has_seal(source_day):
            raise AssertionError(f"Fixture day {source_day} did not seal: {summary.failures}")
    for path in root.iterdir():
        if path.name == "facts":
            continue
        if path.is_dir():
            shutil.rmtree(path)
        else:
            path.unlink()
    return FactStore(root)


def assert_golden(path: Path, content: bytes) -> None:
    """Compare ``content`` with a committed JSON golden, or rewrite it on request.

    ``BPP_UPDATE_GOLDENS=1`` (``just analyzer::golden``) writes the file instead.
    """
    if os.environ.get("BPP_UPDATE_GOLDENS") == "1":
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(content)
        return
    hint = f"{path.name} differs; regenerate with `just analyzer::golden` and review the diff"
    assert path.is_file(), hint
    expected = path.read_bytes()
    if content == expected:
        return
    diff = difflib.unified_diff(
        _pretty(expected), _pretty(content), "committed", "observed", n=1, lineterm=""
    )
    raise AssertionError("\n".join([hint, *list(diff)[:60]]))


def _pretty(content: bytes) -> list[str]:
    return json.dumps(json.loads(content), indent=1, sort_keys=True).splitlines()
