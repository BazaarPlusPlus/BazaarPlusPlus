import gzip
import hashlib
import json
import struct
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


def payload(
    *,
    run_id: str = "run-1",
    account_id: str = "account-1",
    hero: str = "Vanessa",
    final_rank: str | None = "Legendary",
    started_at: str = "2026-08-10T12:00:00Z",
    battle_at: str = "2026-08-10T12:10:00Z",
    winner_combatant_id: str | None = None,
    loser_combatant_id: str | None = None,
    victories: int = 10,
    losses: int = 0,
    cards_per_set: int = 1,
) -> bytes:
    player = [account_id, "Alice", hero, final_rank, 1100, 10, 2, 10, 8, 12, 1, 1]
    opponent = ["account-2", "Bob", "Pygmalien", "Gold", 1050, 10, 2, 8, 8, 12, 1, 1]
    cards = [
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
        for index in range(cards_per_set)
    ]
    battle = [
        "battle-1",
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
                ["player_hand", "Complete", "capture", cards],
                ["player_skills", "Complete", "capture", cards],
                ["opponent_hand", "Complete", "capture", cards],
                ["opponent_skills", "Complete", "capture", cards],
            ]
        ],
        [1, b"spawn", b"combat", b"despawn"],
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
            "2026-08-10T12:30:00Z",
            "completed",
            10,
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
        [battle],
        ["battle-1"],
        [[], [], 0, False],
    ]
    return gzip.compress(_msgpack(root), mtime=0)


def bundle_bytes(
    bundle_id: str,
    *,
    run_payload: bytes | None = None,
    created_at_ms: int | None = None,
) -> bytes:
    content = run_payload or payload()
    manifest = {
        "bundle_version": 5,
        "bundle_id": bundle_id,
        "created_at_ms": created_at_ms or int(SOURCE_HOUR.timestamp() * 1_000),
        "run": {
            "run_format_version": 5,
            "run_id": "run-1",
            "player_account_id": "account-1",
            "projection": {"run": {}, "battles": [{"battle_id": "battle-1"}]},
            "payload": {
                "offset": 0,
                "length": len(content),
                "sha256": hashlib.sha256(content).hexdigest(),
                "content_type": "application/x-bpp-run-v5",
            },
        },
    }
    encoded = json.dumps(manifest, separators=(",", ":")).encode()
    return b"".join(
        [
            b"BPPBNDL5",
            (5).to_bytes(4, "big"),
            len(encoded).to_bytes(4, "big"),
            encoded,
            content,
        ]
    )
