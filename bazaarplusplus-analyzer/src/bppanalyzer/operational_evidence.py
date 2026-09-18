"""Build and persist local Operational Evidence for pipeline Runs."""

import json
import os
import resource
import shutil
import sys
import tempfile
from collections.abc import Callable
from dataclasses import asdict, dataclass
from datetime import UTC, date, datetime, timedelta
from pathlib import Path
from typing import Any, TypedDict

from bppanalyzer.fact_store import FactPruneReport, FactStore, parse_source_day
from bppanalyzer.hour_intake import is_hour_settled
from bppanalyzer.publication import AnalysisWindow, BuildStats, FactStats, HeroStats


class WindowReport(TypedDict):
    start: str
    end: str
    days: int


class DownloadReport(TypedDict):
    expected_bundles: int
    succeeded_bundles: int
    failed_bundles: int
    listing_pages: int
    listing_requests: int
    listing_retries: int
    download_attempts: int
    download_retries: int
    downloaded_bytes: int
    download_latency_ms_p50: float | None
    download_latency_ms_p95: float | None


class FactReport(TypedDict):
    raw_runs: int
    discarded_unknown_hero: int
    discarded_unknown_final_rank: int
    included_runs: int
    included_battles: int


class HeroReport(TypedDict):
    participating_runs: int
    participating_matchup_battles: int
    published: bool


class BuildReport(TypedDict):
    eligible_layout_runs: int
    candidate_builds: int
    published_builds: int
    published: bool


class RetentionReport(TypedDict):
    source_days_pruned: int
    hours_pruned: int
    files_pruned: int
    bytes_pruned: int


class RunReportValue(TypedDict):
    window: WindowReport | None
    downloads: DownloadReport
    facts: FactReport
    heroes: HeroReport
    builds: BuildReport
    retention: RetentionReport


@dataclass(slots=True)
class RunReport:
    value: RunReportValue

    @classmethod
    def empty(
        cls,
        window: AnalysisWindow | None,
        *,
        expected_bundles: int,
        succeeded_bundles: int,
        failed_bundles: int,
    ) -> "RunReport":
        window_report: WindowReport | None = None
        if window is not None:
            window_report = {
                "start": window.start.isoformat(),
                "end": window.end.isoformat(),
                "days": len(window.seals),
            }
        value: RunReportValue = {
            "window": window_report,
            "downloads": {
                "expected_bundles": expected_bundles,
                "succeeded_bundles": succeeded_bundles,
                "failed_bundles": failed_bundles,
                "listing_pages": 0,
                "listing_requests": 0,
                "listing_retries": 0,
                "download_attempts": 0,
                "download_retries": 0,
                "downloaded_bytes": 0,
                "download_latency_ms_p50": None,
                "download_latency_ms_p95": None,
            },
            "facts": {
                "raw_runs": 0,
                "discarded_unknown_hero": 0,
                "discarded_unknown_final_rank": 0,
                "included_runs": 0,
                "included_battles": 0,
            },
            "heroes": {
                "participating_runs": 0,
                "participating_matchup_battles": 0,
                "published": False,
            },
            "builds": {
                "eligible_layout_runs": 0,
                "candidate_builds": 0,
                "published_builds": 0,
                "published": False,
            },
            "retention": {
                "source_days_pruned": 0,
                "hours_pruned": 0,
                "files_pruned": 0,
                "bytes_pruned": 0,
            },
        }
        return cls(value)

    def record_facts(self, stats: FactStats) -> None:
        self.value["facts"] = {
            "raw_runs": stats.raw_runs,
            "discarded_unknown_hero": stats.discarded_unknown_hero,
            "discarded_unknown_final_rank": stats.discarded_unknown_final_rank,
            "included_runs": stats.included_runs,
            "included_battles": stats.included_battles,
        }

    def record_downloads(
        self,
        *,
        listing_pages: int,
        listing_requests: int,
        listing_retries: int,
        download_attempts: int,
        download_retries: int,
        downloaded_bytes: int,
        download_latency_ms_p50: float | None,
        download_latency_ms_p95: float | None,
    ) -> None:
        self.value["downloads"].update(
            listing_pages=listing_pages,
            listing_requests=listing_requests,
            listing_retries=listing_retries,
            download_attempts=download_attempts,
            download_retries=download_retries,
            downloaded_bytes=downloaded_bytes,
            download_latency_ms_p50=download_latency_ms_p50,
            download_latency_ms_p95=download_latency_ms_p95,
        )

    def record_product(self, stats: HeroStats | BuildStats) -> None:
        if isinstance(stats, HeroStats):
            self.value["heroes"].update(
                participating_runs=stats.participating_runs,
                participating_matchup_battles=stats.participating_matchup_battles,
            )
        else:
            self.value["builds"].update(
                eligible_layout_runs=stats.eligible_layout_runs,
                candidate_builds=stats.candidate_builds,
                published_builds=stats.published_builds,
            )

    def mark_published(self, product: str) -> None:
        if product == "heroes":
            self.value["heroes"]["published"] = True
        elif product == "builds":
            self.value["builds"]["published"] = True
        else:
            raise ValueError(f"Unknown consumer product: {product}")

    def record_retention(self, report: FactPruneReport) -> None:
        self.value["retention"] = {
            "source_days_pruned": len(report.source_days),
            "hours_pruned": report.hours_pruned,
            "files_pruned": report.files_pruned,
            "bytes_pruned": report.bytes_pruned,
        }


@dataclass(frozen=True, slots=True)
class RunSummary:
    run_id: str
    started_at: str
    finished_at: str
    timings: dict[str, float]
    outcome: str
    exit_code: int
    hours_ingested: int
    days_sealed: int
    days_abandoned: int
    failures: tuple[dict[str, str], ...]
    report: RunReportValue
    peak_rss_bytes: int

    def to_dict(self) -> dict[str, Any]:
        value = asdict(self)
        value["failures"] = list(self.failures)
        return value


@dataclass(frozen=True, slots=True)
class CurrentRun:
    phase: str
    step: str
    current_hour: str | None
    hours_done: int
    hours_planned: int
    bundles_done: int | None
    bundles_total: int | None
    started_at: datetime
    updated_at: datetime


class OperationalEvidence:
    """Own status, Run history, and log persistence behind one interface."""

    def __init__(
        self,
        root: str | Path,
        run_id: str,
        *,
        clock: Callable[[], datetime],
        ownership_check: Callable[[], None],
    ) -> None:
        self._root = Path(root)
        self._run_id = run_id
        self._clock = clock
        self._ownership_check = ownership_check
        directory = self._root / "logs"
        ownership_check()
        directory.mkdir(parents=True, exist_ok=True)
        stamp = _aware_utc(clock()).strftime("%Y%m%dT%H%M%S.%fZ")
        self._log_path = directory / f"{stamp}-{os.getpid()}-{run_id[:8]}.log"

    def log(self, message: str) -> None:
        self._ownership_check()
        with self._log_path.open("a", encoding="utf-8") as stream:
            stream.write(f"{_timestamp(self._clock())} {message}\n")
            stream.flush()
            os.fsync(stream.fileno())

    def try_log(self, message: str) -> None:
        try:
            self.log(message)
        except BaseException:
            pass

    def checkpoint(
        self,
        store: FactStore,
        current: CurrentRun,
        *,
        considered_days: tuple[date, ...],
        source_epoch: date | None,
    ) -> None:
        previous = _read_object(self._root / "status.json") or {}
        status = _build_status(
            self._root,
            store,
            None,
            now=current.updated_at,
            considered_days=considered_days,
            source_epoch=source_epoch,
        )
        status["last_run"] = previous.get("last_run")
        current_value: dict[str, Any] = {
            "run_id": self._run_id,
            "phase": current.phase,
            "step": current.step,
            "current_hour": current.current_hour,
            "hours_done": current.hours_done,
            "hours_planned": current.hours_planned,
            "started_at": _timestamp(current.started_at),
            "updated_at": _timestamp(current.updated_at),
        }
        if current.bundles_total is not None:
            current_value["bundles"] = {
                "done": current.bundles_done or 0,
                "total": current.bundles_total,
            }
        status["current_run"] = current_value
        _write_status(self._root, status, self._ownership_check)

    def finish(
        self,
        store: FactStore,
        summary: RunSummary,
        *,
        now: datetime,
        considered_days: tuple[date, ...],
        source_epoch: date | None,
        ownership_check: Callable[[], None] | None = None,
    ) -> None:
        check = ownership_check or self._ownership_check
        check()
        status = _build_status(
            self._root,
            store,
            summary,
            now=now,
            considered_days=considered_days,
            source_epoch=source_epoch,
        )
        _write_status(self._root, status, check)
        _append_run(self._root, summary.to_dict(), check)
        _prune_logs(self._root, now, check)


def read_status(data_root: str | Path, *, source_epoch: date | str | None = None) -> dict[str, Any]:
    path = Path(data_root) / "status.json"
    try:
        value = json.loads(path.read_bytes())
    except FileNotFoundError:
        return _build_status(
            data_root,
            FactStore(data_root),
            None,
            now=datetime.now(UTC),
            source_epoch=source_epoch,
        )
    except (OSError, json.JSONDecodeError, UnicodeDecodeError) as error:
        raise RuntimeError("status.json is unreadable") from error
    if not isinstance(value, dict):
        raise RuntimeError("status.json must contain an object")
    return value


def peak_rss_bytes() -> int:
    observed = int(resource.getrusage(resource.RUSAGE_SELF).ru_maxrss)
    return observed if sys.platform == "darwin" else observed * 1024


def _build_status(
    data_root: str | Path,
    store: FactStore,
    last_run: RunSummary | None,
    *,
    now: datetime,
    considered_days: tuple[date, ...] | None = None,
    source_epoch: date | str | None = None,
) -> dict[str, Any]:
    root = Path(data_root)
    epoch = parse_source_day(source_epoch) if source_epoch is not None else None
    seals = tuple(
        item
        for item in store.seals()
        if epoch is None or parse_source_day(item.source_day) >= epoch
    )
    abandoned = tuple(
        item
        for item in store.abandoned_days()
        if epoch is None or parse_source_day(item.source_day) >= epoch
    )
    if considered_days is None:
        candidate_days = {date.fromisoformat(value[:10]) for value in store.committed_hours()}
        candidate_days.update(date.fromisoformat(item.source_day) for item in abandoned)
        considered_days = tuple(
            sorted(day for day in candidate_days if epoch is None or day >= epoch)
        )
    elif epoch is not None:
        considered_days = tuple(day for day in considered_days if day >= epoch)
    sealed_days = {item.source_day for item in seals}
    abandoned_days = {item.source_day for item in abandoned}
    incomplete = []
    for day in considered_days:
        if day.isoformat() in sealed_days or day.isoformat() in abandoned_days:
            continue
        missing = store.missing_hours(day)
        if missing:
            incomplete.append(
                {
                    "source_day": day.isoformat(),
                    "missing_hours": [hour.strftime("%Y-%m-%dT%H") for hour in missing],
                    "settled": is_hour_settled(
                        datetime.combine(day, datetime.min.time(), UTC) + timedelta(hours=23),
                        now,
                    ),
                }
            )
    return {
        "facts": {
            "newest_sealed_day": max(sealed_days, default=None),
            "sealed_days": sorted(sealed_days),
            "incomplete_days": incomplete,
            "abandoned_days": [
                {
                    "source_day": item.source_day,
                    "missing_hours": list(item.missing_hours),
                    "reason": item.reason,
                    "abandoned_at": item.abandoned_at,
                }
                for item in abandoned
            ],
        },
        "publication": {
            product: _local_snapshot_status(root, product) for product in ("heroes", "builds")
        },
        "last_run": last_run.to_dict() if last_run is not None else None,
        "current_run": None,
        "disk": {"free_bytes": shutil.disk_usage(_existing_ancestor(root)).free},
        "peak_rss_bytes": peak_rss_bytes(),
    }


def _local_snapshot_status(root: Path, product: str) -> dict[str, object]:
    path = root / "snapshots" / product / "latest.json"
    try:
        payload = json.loads(path.read_bytes())
        window_end = payload["window"]["end"]
    except FileNotFoundError, OSError, KeyError, TypeError, json.JSONDecodeError:
        return {"present": False, "window_end": None}
    return {"present": True, "window_end": window_end}


def _read_object(path: Path) -> dict[str, Any] | None:
    try:
        value = json.loads(path.read_bytes())
    except FileNotFoundError, OSError, json.JSONDecodeError, UnicodeDecodeError:
        return None
    return value if isinstance(value, dict) else None


def _write_status(root: Path, value: dict[str, Any], ownership_check: Callable[[], None]) -> None:
    ownership_check()
    root.mkdir(parents=True, exist_ok=True)
    descriptor, name = tempfile.mkstemp(prefix=".status.json.tmp-", dir=root)
    temporary = Path(name)
    try:
        with os.fdopen(descriptor, "wb") as stream:
            descriptor = -1
            stream.write(_canonical_json(value))
            stream.flush()
            os.fsync(stream.fileno())
        ownership_check()
        os.replace(temporary, root / "status.json")
        _fsync_directory(root)
    finally:
        if descriptor >= 0:
            os.close(descriptor)
        temporary.unlink(missing_ok=True)


def _append_run(root: Path, value: dict[str, Any], ownership_check: Callable[[], None]) -> None:
    ownership_check()
    root.mkdir(parents=True, exist_ok=True)
    with (root / "runs.jsonl").open("ab") as stream:
        stream.write(_canonical_json(value))
        stream.flush()
        os.fsync(stream.fileno())


def _canonical_json(value: object) -> bytes:
    return (
        json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False) + "\n"
    ).encode("utf-8")


def _timestamp(value: datetime) -> str:
    return _aware_utc(value).isoformat(timespec="seconds").replace("+00:00", "Z")


def _aware_utc(value: datetime) -> datetime:
    if not isinstance(value, datetime) or value.tzinfo is None or value.utcoffset() is None:
        raise ValueError("Pipeline clock must be timezone-aware")
    return value.astimezone(UTC)


def _existing_ancestor(path: Path) -> Path:
    candidate = path
    while not candidate.exists():
        if candidate.parent == candidate:
            return Path("/")
        candidate = candidate.parent
    return candidate


def _fsync_directory(path: Path) -> None:
    descriptor = os.open(path, os.O_RDONLY)
    try:
        os.fsync(descriptor)
    finally:
        os.close(descriptor)


def _prune_logs(root: Path, now: datetime, ownership_check: Callable[[], None]) -> None:
    cutoff = _aware_utc(now).timestamp() - 10 * 24 * 60 * 60
    directory = root / "logs"
    if not directory.is_dir():
        return
    for path in directory.iterdir():
        if not path.is_file() or path.suffix != ".log":
            continue
        try:
            expired = path.stat().st_mtime < cutoff
        except OSError:
            continue
        if expired:
            ownership_check()
            path.unlink(missing_ok=True)
