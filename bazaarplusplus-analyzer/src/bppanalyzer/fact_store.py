"""Atomic, immutable storage for hourly Parquet facts and Source Day seals."""

import hashlib
import json
import os
import re
import shutil
import tempfile
import time
import uuid
from collections.abc import Callable, Iterable, Mapping
from contextlib import ExitStack
from dataclasses import dataclass, replace
from datetime import UTC, date, datetime, timedelta
from pathlib import Path

import pyarrow as pa
import pyarrow.compute as pc
import pyarrow.parquet as pq

from bppanalyzer.bundle_source import parse_source_hour
from bppanalyzer.projection import HourProjection, table_schemas

TABLES = ("runs", "battles", "battle_cards", "quality", "quarantine")
_SHA256 = re.compile(r"^[0-9a-f]{64}$")
_HOUR_DIR = re.compile(r"^source_hour=(\d{4}-\d{2}-\d{2}T\d{2})$")
_SEAL_FILE = re.compile(r"^source_day=(\d{4}-\d{2}-\d{2})\.json$")
_ABANDON_FILE = re.compile(r"^source_day=(\d{4}-\d{2}-\d{2})\.abandoned\.json$")


class FactStoreError(RuntimeError):
    """Base failure for immutable fact state."""


class FactMissing(FactStoreError):
    """Required committed fact state is absent."""


class FactCorrupt(FactStoreError):
    """Committed fact state fails its integrity contract."""


class FactConflict(FactStoreError):
    """An immutable identity already has different canonical bytes."""


@dataclass(frozen=True, slots=True)
class HourCommit:
    source_hour: str
    source_day: str
    raw_commit_sha256: str
    fact_commit_sha256: str
    projection_code_version: str
    bundle_count: int
    row_counts: Mapping[str, int]
    file_sha256s: Mapping[str, str]
    file_bytes: Mapping[str, int]
    reused: bool
    batch_generation_seconds: float = 0.0
    parquet_write_seconds: float = 0.0
    fact_finalize_seconds: float = 0.0


@dataclass(frozen=True, slots=True)
class DaySeal:
    source_day: str
    hourly_fact_commits: tuple[Mapping[str, str], ...]
    row_counts: Mapping[str, int]
    day_seal_sha256: str
    reused: bool = False


@dataclass(frozen=True, slots=True)
class AbandonedDay:
    source_day: str
    missing_hours: tuple[str, ...]
    reason: str
    abandoned_at: str
    reused: bool = False


@dataclass(frozen=True, slots=True)
class VerifyReport:
    days: tuple[str, ...]
    hours_verified: int
    files_verified: int
    deep: bool


@dataclass(frozen=True, slots=True)
class FactPruneReport:
    source_days: tuple[str, ...]
    hours_pruned: int
    files_pruned: int
    bytes_pruned: int


class FactStore:
    """The only owner of paths beneath ``facts/``."""

    def __init__(
        self,
        root: str | Path,
        *,
        clock: Callable[[], datetime] = lambda: datetime.now(UTC),
        ownership_check: Callable[[], None] = lambda: None,
        fault_injector: Callable[[str, Path], None] | None = None,
    ) -> None:
        self.root = Path(root)
        self._hourly = self.root / "facts" / "hourly"
        self._sealed = self.root / "facts" / "sealed"
        self._clock = clock
        self._ownership_check = ownership_check
        self._fault = fault_injector or (lambda _seam, _path: None)

    def commit_hour(self, projected: HourProjection) -> HourCommit:
        commit_started = time.perf_counter()
        self._ownership_check()
        hour = parse_source_hour(projected.source_hour)
        hour_key = hour.strftime("%Y-%m-%dT%H")
        day_key = hour.strftime("%Y-%m-%d")
        if set(projected.table_names) != set(TABLES):
            raise FactCorrupt("Hourly projection must contain exactly five tables")
        schemas = table_schemas()
        projected_schemas = projected.schemas
        for name in TABLES:
            if projected_schemas[name] != schemas[name]:
                raise FactCorrupt(f"Hourly {name} schema differs from the owned schema")

        self._hourly.mkdir(parents=True, exist_ok=True)
        final = self._hourly / f"source_hour={hour_key}"
        for stale in self._hourly.glob(f".{final.name}.tmp-*"):
            if stale.is_dir():
                shutil.rmtree(stale, ignore_errors=True)
        stage = Path(tempfile.mkdtemp(prefix=f".{final.name}.tmp-", dir=self._hourly))
        try:
            file_hashes: dict[str, str] = {}
            file_bytes: dict[str, int] = {}
            row_counts = {name: 0 for name in TABLES}
            generation_seconds = 0.0
            parquet_seconds = 0.0
            with ExitStack() as writer_stack:
                writers = {
                    name: writer_stack.enter_context(
                        pq.ParquetWriter(
                            stage / f"{name}.parquet",
                            schemas[name],
                            compression="zstd",
                            version="2.6",
                            data_page_version="2.0",
                            use_dictionary=True,
                            write_statistics=True,
                        )
                    )
                    for name in TABLES
                }
                batches = projected.iter_batches()
                while True:
                    generation_started = time.perf_counter()
                    try:
                        name, batch = next(batches)
                    except StopIteration:
                        break
                    generation_seconds += time.perf_counter() - generation_started
                    self._ownership_check()
                    if name not in writers:
                        raise FactCorrupt(f"Hourly projection emitted unknown table {name}")
                    if batch.schema != schemas[name]:
                        raise FactCorrupt(f"Hourly {name} schema differs from the owned schema")
                    self._require_partition_columns(batch, name, hour_key, day_key)
                    write_started = time.perf_counter()
                    writers[name].write_batch(batch, row_group_size=batch.num_rows)
                    parquet_seconds += time.perf_counter() - write_started
                    row_counts[name] += batch.num_rows

            for name in TABLES:
                path = stage / f"{name}.parquet"
                _fsync_file(path)
                file_hashes[path.name] = _sha256_file(path)
                file_bytes[path.name] = path.stat().st_size

            self._fault("before_precommit_verify", stage)
            for filename, expected in file_hashes.items():
                if _sha256_file(stage / filename) != expected:
                    raise FactCorrupt(
                        f"Staged Parquet checksum differs before promotion: {filename}"
                    )
                if (stage / filename).stat().st_size != file_bytes[filename]:
                    raise FactCorrupt(f"Staged Parquet size differs before promotion: {filename}")

            body = {
                "schema_version": 1,
                "source_hour": hour_key,
                "source_day": day_key,
                "raw_commit_sha256": projected.raw_commit_sha256,
                "projection_code_version": projected.projection_version,
                "bundle_count": projected.bundle_count,
                "row_counts": dict(sorted(row_counts.items())),
                "file_sha256s": dict(sorted(file_hashes.items())),
                "file_bytes": dict(sorted(file_bytes.items())),
            }
            commit_bytes = canonical_json(body)
            _durable_create(stage / "_commit.json", commit_bytes)
            _fsync_directory(stage)
            self._fault("before_hour_promote", stage)
            self._ownership_check()

            if final.exists():
                existing_path = final / "_commit.json"
                try:
                    existing = existing_path.read_bytes()
                except OSError as error:
                    raise FactConflict(
                        f"Existing Hourly Fact Partition is incomplete: {hour_key}"
                    ) from error
                if existing != commit_bytes:
                    raise FactConflict(f"Hourly Fact Partition commit conflict: {hour_key}")
                committed = self._read_hour(hour, deep=True)
                return replace(
                    committed,
                    reused=True,
                    batch_generation_seconds=round(generation_seconds, 6),
                    parquet_write_seconds=round(parquet_seconds, 6),
                    fact_finalize_seconds=round(
                        max(
                            time.perf_counter()
                            - commit_started
                            - generation_seconds
                            - parquet_seconds,
                            0.0,
                        ),
                        6,
                    ),
                )

            os.rename(stage, final)
            _fsync_directory(self._hourly)
            return replace(
                self._read_hour(hour, deep=True),
                reused=False,
                batch_generation_seconds=round(generation_seconds, 6),
                parquet_write_seconds=round(parquet_seconds, 6),
                fact_finalize_seconds=round(
                    max(
                        time.perf_counter() - commit_started - generation_seconds - parquet_seconds,
                        0.0,
                    ),
                    6,
                ),
            )
        finally:
            if stage.exists():
                shutil.rmtree(stage, ignore_errors=True)

    def seal_day(self, source_day: date | str) -> DaySeal:
        day = parse_source_day(source_day)
        day_key = day.isoformat()
        if self.is_abandoned(day):
            raise FactConflict(f"Abandoned Source Day cannot be sealed: {day_key}")
        commits = []
        for hour_number in range(24):
            hour = datetime.combine(day, datetime.min.time(), UTC) + timedelta(hours=hour_number)
            commits.append(self._read_hour(hour, deep=False))
        if [commit.source_hour[-2:] for commit in commits] != [f"{hour:02d}" for hour in range(24)]:
            raise FactCorrupt(f"Source Day does not contain hours 00-23: {day_key}")

        row_counts = {name: sum(commit.row_counts[name] for commit in commits) for name in TABLES}
        body = {
            "schema_version": 1,
            "source_day": day_key,
            "hourly_fact_commits": [
                {
                    "source_hour": commit.source_hour,
                    "fact_commit_sha256": commit.fact_commit_sha256,
                }
                for commit in commits
            ],
            "row_counts": dict(sorted(row_counts.items())),
        }
        seal_hash = hashlib.sha256(canonical_json(body)).hexdigest()
        sealed_body = {**body, "day_seal_sha256": seal_hash}
        content = canonical_json(sealed_body)
        self._ownership_check()
        self._sealed.mkdir(parents=True, exist_ok=True)
        destination = self._seal_path(day)
        self._fault("before_seal_promote", destination)
        self._ownership_check()
        reused = _atomic_immutable_file(destination, content)
        return DaySeal(
            source_day=day_key,
            hourly_fact_commits=tuple(body["hourly_fact_commits"]),
            row_counts=row_counts,
            day_seal_sha256=seal_hash,
            reused=reused,
        )

    def abandon_day(
        self,
        source_day: date | str,
        missing_hours: Iterable[datetime | str],
        reason: str,
    ) -> AbandonedDay:
        day = parse_source_day(source_day)
        day_key = day.isoformat()
        if not reason:
            raise ValueError("Abandonment reason is required")
        if self.has_seal(day):
            raise FactConflict(f"Sealed Source Day cannot be abandoned: {day_key}")
        path = self._abandon_path(day)
        if path.is_file():
            return replace(self._read_abandoned(path), reused=True)
        hours = tuple(
            sorted({parse_source_hour(value).strftime("%Y-%m-%dT%H") for value in missing_hours})
        )
        if not hours or any(not value.startswith(f"{day_key}T") for value in hours):
            raise ValueError("Abandonment must name missing hours from its Source Day")
        now = self._clock()
        if now.tzinfo is None or now.utcoffset() is None:
            raise ValueError("Fact Store clock must be timezone-aware")
        body = {
            "schema_version": 1,
            "source_day": day_key,
            "missing_hours": list(hours),
            "reason": reason,
            "abandoned_at": now.astimezone(UTC).isoformat().replace("+00:00", "Z"),
        }
        self._ownership_check()
        self._sealed.mkdir(parents=True, exist_ok=True)
        reused = _atomic_immutable_file(path, canonical_json(body))
        if reused:
            return replace(self._read_abandoned(path), reused=True)
        return AbandonedDay(day_key, hours, reason, body["abandoned_at"])

    def is_abandoned(self, source_day: date | str) -> bool:
        return self._abandon_path(parse_source_day(source_day)).is_file()

    def abandoned_days(self) -> tuple[AbandonedDay, ...]:
        if not self._sealed.is_dir():
            return ()
        values: list[AbandonedDay] = []
        for path in sorted(self._sealed.iterdir()):
            if _ABANDON_FILE.fullmatch(path.name):
                values.append(self._read_abandoned(path))
        return tuple(values)

    def seals(self) -> tuple[DaySeal, ...]:
        if not self._sealed.is_dir():
            return ()
        values: list[DaySeal] = []
        for path in sorted(self._sealed.iterdir()):
            match = _SEAL_FILE.fullmatch(path.name)
            if match is None:
                continue
            values.append(self._read_seal(path, verify_hours=False))
        return tuple(values)

    def has_seal(self, source_day: date | str) -> bool:
        path = self._seal_path(parse_source_day(source_day))
        if not path.is_file():
            return False
        self._read_seal(path, verify_hours=False)
        return True

    def has_hour(self, source_hour: datetime | str) -> bool:
        hour = parse_source_hour(source_hour)
        path = self._hour_path(hour)
        if not path.exists():
            return False
        self._read_hour(hour, deep=False)
        return True

    def missing_hours(self, source_day: date | str) -> tuple[datetime, ...]:
        day = parse_source_day(source_day)
        missing: list[datetime] = []
        for hour_number in range(24):
            hour = datetime.combine(day, datetime.min.time(), UTC) + timedelta(hours=hour_number)
            if not self.has_hour(hour):
                missing.append(hour)
        return tuple(missing)

    def hour_paths(self, days: Iterable[date | str]) -> Mapping[str, tuple[Path, ...]]:
        paths = {name: [] for name in TABLES}
        for raw_day in days:
            day = parse_source_day(raw_day)
            seal = self._read_seal(self._seal_path(day), verify_hours=True)
            for hourly in seal.hourly_fact_commits:
                hour_path = self._hourly / f"source_hour={hourly['source_hour']}"
                for name in TABLES:
                    paths[name].append(hour_path / f"{name}.parquet")
        return {name: tuple(values) for name, values in paths.items()}

    def verify(self, source_day: date | str | None = None, *, deep: bool = False) -> VerifyReport:
        days = (
            (parse_source_day(source_day),)
            if source_day is not None
            else tuple(date.fromisoformat(seal.source_day) for seal in self.seals())
        )
        hours = 0
        files = 0
        for day in days:
            seal = self._read_seal(self._seal_path(day), verify_hours=False)
            for value in seal.hourly_fact_commits:
                hour = parse_source_hour(value["source_hour"])
                commit = self._read_hour(hour, deep=True)
                if commit.fact_commit_sha256 != value["fact_commit_sha256"]:
                    raise FactCorrupt(
                        f"Day Seal references another Hourly commit: {commit.source_hour}"
                    )
                hours += 1
                files += len(commit.file_sha256s)
        return VerifyReport(tuple(day.isoformat() for day in days), hours, files, deep)

    def committed_hours(self) -> tuple[str, ...]:
        if not self._hourly.is_dir():
            return ()
        result: list[str] = []
        for path in sorted(self._hourly.iterdir()):
            match = _HOUR_DIR.fullmatch(path.name)
            if match is None or not path.is_dir():
                continue
            self._read_hour(parse_source_hour(match.group(1)), deep=False)
            result.append(match.group(1))
        return tuple(result)

    def prune(self, *, retain_days: int) -> FactPruneReport:
        """Remove facts older than the latest retained Complete Source Days."""
        if not isinstance(retain_days, int) or isinstance(retain_days, bool) or retain_days < 1:
            raise ValueError("retain_days must be a positive integer")
        seals = self.seals()
        if len(seals) < retain_days:
            return FactPruneReport((), 0, 0, 0)

        cutoff = parse_source_day(seals[-retain_days].source_day)
        pruned_days: set[str] = set()
        files_pruned = 0
        bytes_pruned = 0

        for seal in seals[:-retain_days]:
            path = self._seal_path(parse_source_day(seal.source_day))
            self._ownership_check()
            try:
                bytes_pruned += path.stat().st_size
            except OSError:
                pass
            path.unlink()
            files_pruned += 1
            pruned_days.add(seal.source_day)
        if seals[:-retain_days]:
            _fsync_directory(self._sealed)

        hours_pruned = 0
        if self._hourly.is_dir():
            for path in sorted(self._hourly.iterdir()):
                match = _HOUR_DIR.fullmatch(path.name)
                if match is None or not path.is_dir():
                    continue
                source_hour = parse_source_hour(match.group(1))
                if source_hour.date() >= cutoff:
                    continue
                self._ownership_check()
                for item in path.iterdir():
                    if not item.is_file():
                        continue
                    try:
                        bytes_pruned += item.stat().st_size
                    except OSError:
                        pass
                    files_pruned += 1
                shutil.rmtree(path)
                hours_pruned += 1
                pruned_days.add(source_hour.date().isoformat())
            if hours_pruned:
                _fsync_directory(self._hourly)

        return FactPruneReport(tuple(sorted(pruned_days)), hours_pruned, files_pruned, bytes_pruned)

    def _read_hour(self, source_hour: datetime | str, *, deep: bool) -> HourCommit:
        hour = parse_source_hour(source_hour)
        hour_key = hour.strftime("%Y-%m-%dT%H")
        day_key = hour.strftime("%Y-%m-%d")
        path = self._hour_path(hour)
        commit_path = path / "_commit.json"
        if not commit_path.is_file():
            raise FactMissing(f"Hourly Fact Partition is missing: {hour_key}")
        try:
            commit_bytes = commit_path.read_bytes()
            value = json.loads(commit_bytes)
        except (OSError, json.JSONDecodeError, UnicodeDecodeError) as error:
            raise FactCorrupt(f"Hourly commit is unreadable: {hour_key}") from error
        if (
            not isinstance(value, dict)
            or value.get("source_hour") != hour_key
            or value.get("source_day") != day_key
        ):
            raise FactCorrupt(f"Hourly commit identity differs: {hour_key}")
        if canonical_json(value) != commit_bytes:
            raise FactCorrupt(f"Hourly commit is not canonical: {hour_key}")
        expected_files = {f"{name}.parquet" for name in TABLES}
        try:
            actual_files = {item.name for item in path.iterdir()}
        except OSError as error:
            raise FactMissing(f"Hourly Fact Partition is missing: {hour_key}") from error
        if actual_files != expected_files | {"_commit.json"}:
            raise FactCorrupt(f"Hourly Fact Partition file set differs: {hour_key}")
        file_hashes = _digest_map(value.get("file_sha256s"), expected_files, "hash")
        file_bytes = _integer_map(value.get("file_bytes"), expected_files, "size")
        row_counts = _integer_map(value.get("row_counts"), set(TABLES), "row count")
        for filename in expected_files:
            try:
                actual_size = (path / filename).stat().st_size
            except OSError as error:
                raise FactCorrupt(f"Hourly Parquet is missing: {hour_key}/{filename}") from error
            if actual_size != file_bytes[filename]:
                raise FactCorrupt(f"Hourly Parquet size differs: {hour_key}/{filename}")
            if deep and _sha256_file(path / filename) != file_hashes[filename]:
                raise FactCorrupt(f"Hourly Parquet checksum differs: {hour_key}/{filename}")
        raw_hash = value.get("raw_commit_sha256")
        version = value.get("projection_code_version")
        bundle_count = value.get("bundle_count")
        if not isinstance(raw_hash, str) or _SHA256.fullmatch(raw_hash) is None:
            raise FactCorrupt(f"Hourly raw identity is invalid: {hour_key}")
        if not isinstance(version, str) or not version:
            raise FactCorrupt(f"Hourly projection version is invalid: {hour_key}")
        if not isinstance(bundle_count, int) or isinstance(bundle_count, bool) or bundle_count < 0:
            raise FactCorrupt(f"Hourly Bundle count is invalid: {hour_key}")
        return HourCommit(
            source_hour=hour_key,
            source_day=day_key,
            raw_commit_sha256=raw_hash,
            fact_commit_sha256=hashlib.sha256(commit_bytes).hexdigest(),
            projection_code_version=version,
            bundle_count=bundle_count,
            row_counts=row_counts,
            file_sha256s=file_hashes,
            file_bytes=file_bytes,
            reused=True,
        )

    def _read_seal(self, path: Path, *, verify_hours: bool) -> DaySeal:
        if not path.is_file():
            raise FactMissing(f"Source Day Seal is missing: {path.name}")
        try:
            content = path.read_bytes()
            value = json.loads(content)
        except (OSError, json.JSONDecodeError, UnicodeDecodeError) as error:
            raise FactCorrupt(f"Source Day Seal is unreadable: {path.name}") from error
        if not isinstance(value, dict) or canonical_json(value) != content:
            raise FactCorrupt(f"Source Day Seal is not canonical: {path.name}")
        day = value.get("source_day")
        try:
            expected_path = self._seal_path(parse_source_day(day)) if isinstance(day, str) else None
        except TypeError, ValueError:
            expected_path = None
        if path != expected_path:
            raise FactCorrupt(f"Source Day Seal identity differs: {path.name}")
        hourly = value.get("hourly_fact_commits")
        expected_hours = [f"{day}T{number:02d}" for number in range(24)]
        observed_hours = (
            [item.get("source_hour") if isinstance(item, dict) else None for item in hourly]
            if isinstance(hourly, list)
            else None
        )
        if observed_hours != expected_hours:
            raise FactCorrupt(f"Source Day Seal must contain hours 00-23: {day}")
        for item in hourly:
            digest = item.get("fact_commit_sha256")
            if not isinstance(digest, str) or _SHA256.fullmatch(digest) is None:
                raise FactCorrupt(f"Source Day Seal has an invalid Hourly digest: {day}")
        row_counts = _integer_map(value.get("row_counts"), set(TABLES), "row count")
        declared = value.get("day_seal_sha256")
        body = {key: item for key, item in value.items() if key != "day_seal_sha256"}
        actual = hashlib.sha256(canonical_json(body)).hexdigest()
        if declared != actual:
            raise FactCorrupt(f"Source Day Seal digest differs: {day}")
        if verify_hours:
            for item in hourly:
                commit = self._read_hour(item["source_hour"], deep=False)
                if commit.fact_commit_sha256 != item["fact_commit_sha256"]:
                    raise FactCorrupt(
                        f"Source Day Seal references another Hourly commit: {commit.source_hour}"
                    )
        return DaySeal(day, tuple(hourly), row_counts, actual, reused=True)

    def _read_abandoned(self, path: Path) -> AbandonedDay:
        try:
            content = path.read_bytes()
            value = json.loads(content)
        except (OSError, json.JSONDecodeError, UnicodeDecodeError) as error:
            raise FactCorrupt(f"Abandonment record is unreadable: {path.name}") from error
        if not isinstance(value, dict) or canonical_json(value) != content:
            raise FactCorrupt(f"Abandonment record is not canonical: {path.name}")
        day = value.get("source_day")
        missing = value.get("missing_hours")
        reason = value.get("reason")
        abandoned_at = value.get("abandoned_at")
        try:
            expected_path = (
                self._abandon_path(parse_source_day(day)) if isinstance(day, str) else None
            )
        except TypeError, ValueError:
            expected_path = None
        if (
            path != expected_path
            or not isinstance(missing, list)
            or not missing
            or missing != sorted(set(missing))
            or any(not isinstance(item, str) or not item.startswith(f"{day}T") for item in missing)
            or not isinstance(reason, str)
            or not reason
            or not isinstance(abandoned_at, str)
        ):
            raise FactCorrupt(f"Abandonment record fields are invalid: {path.name}")
        return AbandonedDay(day, tuple(missing), reason, abandoned_at, reused=True)

    @staticmethod
    def _require_partition_columns(
        batch: pa.RecordBatch, table_name: str, hour_key: str, day_key: str
    ) -> None:
        if batch.num_rows == 0:
            return
        source_hour = batch.column("source_hour")
        if (
            source_hour.null_count or pc.all(pc.equal(source_hour, hour_key)).as_py() is not True  # ty: ignore[unresolved-attribute]
        ):
            raise FactCorrupt(f"{table_name} rows moved outside their Source Hour")
        source_day = batch.column("source_day")
        if (
            source_day.null_count or pc.all(pc.equal(source_day, day_key)).as_py() is not True  # ty: ignore[unresolved-attribute]
        ):
            raise FactCorrupt(f"{table_name} rows moved outside their Source Day")

    def _hour_path(self, hour: datetime) -> Path:
        return self._hourly / f"source_hour={hour.strftime('%Y-%m-%dT%H')}"

    def _seal_path(self, day: date) -> Path:
        return self._sealed / f"source_day={day.isoformat()}.json"

    def _abandon_path(self, day: date) -> Path:
        return self._sealed / f"source_day={day.isoformat()}.abandoned.json"


def parse_source_day(value: date | str) -> date:
    if isinstance(value, datetime):
        raise ValueError("Source Day cannot be a datetime")
    if isinstance(value, str):
        try:
            parsed = date.fromisoformat(value)
        except ValueError as error:
            raise ValueError("Source Day must use YYYY-MM-DD") from error
        if parsed.isoformat() != value:
            raise ValueError("Source Day must use YYYY-MM-DD")
        return parsed
    if not isinstance(value, date):
        raise TypeError("Source Day must be a date or YYYY-MM-DD")
    return value


def canonical_json(value: object) -> bytes:
    return (
        json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False) + "\n"
    ).encode("utf-8")


def _digest_map(value: object, expected: set[str], label: str) -> dict[str, str]:
    if (
        not isinstance(value, dict)
        or set(value) != expected
        or not all(
            isinstance(key, str) and isinstance(item, str) and _SHA256.fullmatch(item) is not None
            for key, item in value.items()
        )
    ):
        raise FactCorrupt(f"Hourly Parquet {label} map is invalid")
    return dict(value)


def _integer_map(value: object, expected: set[str], label: str) -> dict[str, int]:
    if (
        not isinstance(value, dict)
        or set(value) != expected
        or not all(
            isinstance(key, str)
            and isinstance(item, int)
            and not isinstance(item, bool)
            and item >= 0
            for key, item in value.items()
        )
    ):
        raise FactCorrupt(f"Hourly Parquet {label} map is invalid")
    return dict(value)


def _sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _durable_create(path: Path, content: bytes) -> None:
    with path.open("xb") as stream:
        stream.write(content)
        stream.flush()
        os.fsync(stream.fileno())


def _atomic_immutable_file(path: Path, content: bytes) -> bool:
    for stale in path.parent.glob(f".{path.name}.tmp-*"):
        if stale.is_file():
            stale.unlink(missing_ok=True)
    if path.exists():
        try:
            existing = path.read_bytes()
        except OSError as error:
            raise FactConflict(f"Immutable file is unreadable: {path.name}") from error
        if existing == content:
            return True
        raise FactConflict(f"Immutable file conflict: {path.name}")
    temporary = path.parent / f".{path.name}.tmp-{uuid.uuid4().hex}"
    try:
        _durable_create(temporary, content)
        os.link(temporary, path)
        _fsync_directory(path.parent)
    except FileExistsError:
        try:
            if path.read_bytes() == content:
                return True
        except OSError:
            pass
        raise FactConflict(f"Immutable file conflict: {path.name}") from None
    finally:
        temporary.unlink(missing_ok=True)
    return False


def _fsync_file(path: Path) -> None:
    descriptor = os.open(path, os.O_RDONLY)
    try:
        os.fsync(descriptor)
    finally:
        os.close(descriptor)


def _fsync_directory(path: Path) -> None:
    descriptor = os.open(path, os.O_RDONLY)
    try:
        os.fsync(descriptor)
    finally:
        os.close(descriptor)
