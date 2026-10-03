import hashlib
from collections.abc import Iterator, Mapping
from datetime import UTC, datetime

import pyarrow as pa

from bppanalyzer.object_store import ObjectStat, StoredObject
from bppanalyzer.projection import ROW_BATCH_SIZE, HourProjection, table_schemas


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


class TableStream:
    """Duck-typed projection batch stream over prebuilt rows."""

    def __init__(self, rows: Mapping[str, list[dict[str, object]]]) -> None:
        self.tables = {
            name: pa.Table.from_pylist(list(rows.get(name, [])), schema=schema)
            for name, schema in table_schemas().items()
        }
        self.bundle_count = self.tables["runs"].num_rows + self.tables["quarantine"].num_rows

    def __iter__(self) -> Iterator[tuple[str, pa.RecordBatch]]:
        for name, table in self.tables.items():
            for batch in table.to_batches(max_chunksize=ROW_BATCH_SIZE):
                yield name, batch


def row_projection(
    source_hour: datetime,
    raw_commit_sha256: str,
    rows: Mapping[str, list[dict[str, object]]] | None = None,
) -> HourProjection:
    return HourProjection(
        source_hour,
        raw_commit_sha256,
        batch_stream=TableStream(rows or {}),
    )


def collect_tables(projection: HourProjection) -> dict[str, pa.Table]:
    batches: dict[str, list[pa.RecordBatch]] = {name: [] for name in table_schemas()}
    for name, batch in projection.iter_batches():
        batches[name].append(batch)
    return {
        name: pa.Table.from_batches(items, schema=table_schemas()[name])
        for name, items in batches.items()
    }
