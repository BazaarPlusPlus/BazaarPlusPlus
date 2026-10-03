import hashlib
import json
import struct
import zlib
from collections.abc import Sequence
from dataclasses import dataclass
from datetime import UTC, datetime

SOURCE_HOUR = datetime(2026, 8, 10, 12, tzinfo=UTC)


def _msgpack(value: object) -> bytes:
    if value is None:
        return b"\xc0"
    if value is False:
        return b"\xc2"
    if value is True:
        return b"\xc3"
    if isinstance(value, int):
        if 0 <= value <= 127:
            return bytes((value,))
        if -32 <= value < 0:
            return bytes((256 + value,))
        if 0 <= value <= 0xFF:
            return b"\xcc" + struct.pack(">B", value)
        if 0 <= value <= 0xFFFF:
            return b"\xcd" + struct.pack(">H", value)
        if 0 <= value <= 0xFFFFFFFF:
            return b"\xce" + struct.pack(">I", value)
        return b"\xd3" + struct.pack(">q", value)
    if isinstance(value, bytes):
        if len(value) <= 0xFF:
            return b"\xc4" + struct.pack(">B", len(value)) + value
        return b"\xc5" + struct.pack(">H", len(value)) + value
    if isinstance(value, str):
        encoded = value.encode()
        if len(encoded) < 32:
            return bytes((0xA0 | len(encoded),)) + encoded
        if len(encoded) <= 0xFF:
            return b"\xd9" + struct.pack(">B", len(encoded)) + encoded
        return b"\xda" + struct.pack(">H", len(encoded)) + encoded
    if isinstance(value, list):
        prefix = (
            bytes((0x90 | len(value),))
            if len(value) < 16
            else b"\xdc" + struct.pack(">H", len(value))
        )
        return prefix + b"".join(_msgpack(item) for item in value)
    if isinstance(value, dict):
        prefix = (
            bytes((0x80 | len(value),))
            if len(value) < 16
            else b"\xde" + struct.pack(">H", len(value))
        )
        return prefix + b"".join(_msgpack(key) + _msgpack(item) for key, item in value.items())
    raise TypeError(type(value).__name__)


@dataclass(frozen=True, slots=True)
class Card:
    """One player-hand card; slot doubles as the Bundle ``socket``."""

    template_id: str
    size: int
    slot: int
    tier: str | None = "Gold"
    enchantment: str | None = None
    card_type: int = 0
    name: str = "Fixture Item"


def _default_cards(count: int) -> list[list[object]]:
    return [
        [
            f"instance-{index + 1}",
            "item-one",
            1,
            2,
            1,
            3,
            "Item One",
            "Gold",
            None,
            ["Weapon"],
            {"damage": 12},
        ]
        for index in range(count)
    ]


def _card_row(index: int, card: Card) -> list[object]:
    return [
        f"instance-{index + 1}",
        card.template_id,
        card.card_type,
        card.size,
        1,
        card.slot,
        card.name,
        card.tier,
        card.enchantment,
        ["Weapon"],
        {},
    ]


def payload(
    *,
    run_id: str = "run-1",
    account_id: str = "account-1",
    hero: str = "Vanessa",
    final_rank: str | None = "Legendary",
    started_at: str = "2026-08-10T12:00:00Z",
    battle_at: str = "2026-08-10T12:10:00Z",
    ended_at: str = "2026-08-10T12:30:00Z",
    winner_combatant_id: str | None = None,
    loser_combatant_id: str | None = None,
    victories: int = 10,
    losses: int = 0,
    run_day: int = 10,
    cards_per_set: int = 1,
    player_hand: Sequence[Card] | None = None,
    player_hand_status: str = "Complete",
    final_battles: int = 1,
) -> bytes:
    """Encode one Run payload; ``final_battles`` Battles are all flagged final."""
    player = [account_id, "Alice", hero, final_rank, 1100, 10, 2, 10, 8, 12, 1, 1]
    opponent = ["account-2", "Bob", "Pygmalien", "Gold", 1050, 10, 2, 8, 8, 12, 1, 1]
    cards = _default_cards(cards_per_set)
    hand = (
        cards
        if player_hand is None
        else [_card_row(index, card) for index, card in enumerate(player_hand)]
    )
    battles = [
        [
            battle_id,
            [
                battle_at,
                10,
                2,
                "encounter-1",
                "PvP",
                "Win",
                winner_combatant_id or account_id,
                loser_combatant_id or "account-2",
                True,
            ],
            [player, opponent],
            [
                [
                    ["player_hand", player_hand_status, "capture", hand],
                    ["player_skills", "Complete", "capture", cards],
                    ["opponent_hand", "Complete", "capture", cards],
                    ["opponent_skills", "Complete", "capture", cards],
                ]
            ],
            [1, b"spawn", b"combat", b"despawn"],
        ]
        for battle_id in battle_ids(final_battles)
    ]
    root = [
        5,
        run_id,
        account_id,
        [
            hero,
            "Ranked",
            42,
            started_at,
            ended_at,
            "completed",
            run_day,
            2,
            victories,
            losses,
            "Gold",
            1000,
            final_rank,
            1100,
            100,
            50,
            2,
            10,
            8,
            12,
            "stable",
            "5.1.0",
        ],
        [],
        battles,
        ["battle-1"],
        [[], [], 0, False],
    ]
    return _stored_gzip(_msgpack(root))


def _stored_gzip(data: bytes) -> bytes:
    """Gzip with uncompressed DEFLATE blocks.

    Compressed output differs between zlib builds; stored blocks keep fixture
    Bundles, and every golden that hashes them, byte-identical on every platform.
    """
    blocks = []
    for start in range(0, len(data), 0xFFFF):
        chunk = data[start : start + 0xFFFF]
        final = start + 0xFFFF >= len(data)
        blocks.append(bytes((final,)) + struct.pack("<HH", len(chunk), len(chunk) ^ 0xFFFF) + chunk)
    trailer = struct.pack("<II", zlib.crc32(data), len(data) & 0xFFFFFFFF)
    return b"\x1f\x8b\x08\x00\x00\x00\x00\x00\x00\xff" + b"".join(blocks) + trailer


def battle_ids(count: int = 1) -> list[str]:
    return [f"battle-{number}" for number in range(1, count + 1)]


def bundle_bytes(
    bundle_id: str,
    *,
    run_payload: bytes | None = None,
    created_at_ms: int | None = None,
    run_id: str = "run-1",
    account_id: str = "account-1",
) -> bytes:
    """Wrap a Run payload whose identities match ``run_id`` and ``account_id``."""
    content = run_payload or payload()
    manifest = {
        "bundle_version": 5,
        "bundle_id": bundle_id,
        "created_at_ms": created_at_ms or int(SOURCE_HOUR.timestamp() * 1_000),
        "run": {
            "run_format_version": 5,
            "run_id": run_id,
            "player_account_id": account_id,
            "projection": {"run": {}, "battles": [{"battle_id": "battle-1"}]},
            "payload": {
                "offset": 0,
                "length": len(content),
                "sha256": hashlib.sha256(content).hexdigest(),
                "content_type": "application/x-bpp-run-v5",
            },
        },
    }
    return envelope(manifest, content)


def envelope(manifest: dict[str, object], run_content: bytes, *, sort_keys: bool = False) -> bytes:
    encoded = json.dumps(manifest, sort_keys=sort_keys, separators=(",", ":")).encode()
    return b"".join(
        [
            b"BPPBNDL5",
            (5).to_bytes(4, "big"),
            len(encoded).to_bytes(4, "big"),
            encoded,
            run_content,
        ]
    )
