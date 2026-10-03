import hashlib
from collections.abc import Iterator
from datetime import UTC, datetime

import pyarrow as pa

from bppanalyzer.object_store import ObjectStat, StoredObject
from bppanalyzer.projection import HourProjection, table_schemas


class MemoryObjectStore:
    """Dict-backed ``ObjectStore`` that records every request in order."""

    def __init__(self) -> None:
        self.objects: dict[str, StoredObject] = {}
        self.requests: list[tuple[str, str]] = []

    def stat(self, key: str) -> ObjectStat | None:
        self.requests.append(("stat", key))
        stored = self.objects.get(key)
        return stored.stat if stored is not None else None

    def get(self, key: str) -> StoredObject | None:
        self.requests.append(("get", key))
        return self.objects.get(key)

    def put(
        self,
        key: str,
        body: bytes,
        *,
        cache_control: str,
        content_type: str = "application/octet-stream",
    ) -> None:
        self.requests.append(("put", key))
        stat = ObjectStat(
            key,
            hashlib.sha256(body).hexdigest(),
            len(body),
            cache_control,
            content_type,
            datetime.now(UTC),
        )
        self.objects[key] = StoredObject(body, stat)

    def put_keys(self) -> list[str]:
        return [key for operation, key in self.requests if operation == "put"]


class _NoBatches:
    """Projection batch stream of an hour that listed no Bundles."""

    bundle_count = 0

    def __iter__(self) -> Iterator[tuple[str, pa.RecordBatch]]:
        return iter(())


def empty_projection(source_hour: datetime, raw_commit_sha256: str) -> HourProjection:
    return HourProjection(source_hour, raw_commit_sha256, batch_stream=_NoBatches())


def collect_tables(projection: HourProjection) -> dict[str, pa.Table]:
    batches: dict[str, list[pa.RecordBatch]] = {name: [] for name in table_schemas()}
    for name, batch in projection.iter_batches():
        batches[name].append(batch)
    return {
        name: pa.Table.from_batches(items, schema=table_schemas()[name])
        for name, items in batches.items()
    }
