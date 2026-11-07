"""Decode the owned Run payload V6 without depending on fact storage."""

import gzip
import struct
from collections.abc import Callable, Mapping
from datetime import UTC, datetime, timedelta
from io import BytesIO
from typing import Any, cast

MAX_DECOMPRESSED_RUN_BYTES = 64 * 1024 * 1024


class RunPayloadError(ValueError):
    """A stable, deterministic reason to quarantine one Bundle."""

    def __init__(self, reason: str, message: str) -> None:
        super().__init__(message)
        self.reason = reason


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
    version = _integer(root[0], "payload.version")
    if version != 6:
        raise RunPayloadError("payload_version_unsupported", "Run version is unsupported")
    return _v6_times_as_text(root)


_UNIX_EPOCH = datetime(1970, 1, 1, tzinfo=UTC)


def _v6_times_as_text(root: tuple[Any, ...]) -> tuple[Any, ...]:
    """Return a V6 root whose time slots hold UTC text.

    V6 carries Run start/end and Battle record times as Unix milliseconds; the fact
    columns store them as millisecond-precision text (``*_at_utc``).
    """
    try:
        run = _with_slot(root[3], 22, "run", 3, _ms_text)
        run = _with_slot(
            run, 22, "run", 4, lambda value: None if value is None else _ms_text(value)
        )
        battles = [
            _with_slot(
                battle,
                5,
                "battle",
                1,
                lambda facts: _with_slot(facts, 9, "battle.facts", 0, _ms_text),
            )
            for battle in _array(root[5], "payload.battles")
        ]
    except (ValueError, TypeError, OverflowError) as error:
        raise RunPayloadError("run_payload_decode_failed", "Run V6 times are invalid") from error
    return (*root[:3], run, root[4], battles, *root[6:])


def _with_slot(
    value: object, count: int, field: str, index: int, convert: Callable[[Any], object]
) -> tuple[Any, ...]:
    slots = list(_slots(value, count, field))
    slots[index] = convert(slots[index])
    return tuple(slots)


def _ms_text(value: object) -> str:
    milliseconds = _integer(value, "time_ms")
    instant = _UNIX_EPOCH + timedelta(milliseconds=milliseconds)
    return instant.isoformat(timespec="milliseconds").replace("+00:00", "Z")


class _MessagePack:
    """Small strict decoder for the scalar/array/map subset used by RunPayload V6."""

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
    raise ValueError(f"{field} must be an array")


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
