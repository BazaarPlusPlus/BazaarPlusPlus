"""Heal complete Source Days and independently publish two consumer snapshots."""

import json
import time
import uuid
from collections.abc import Callable
from datetime import UTC, date, datetime
from functools import partial
from pathlib import Path

from bppanalyzer.bundle_source import (
    HourExpired,
    RetryableSourceError,
)
from bppanalyzer.config import MIN_FACT_RETENTION_DAYS
from bppanalyzer.durable import atomic_replace, aware_utc
from bppanalyzer.fact_store import FactStore, parse_source_day
from bppanalyzer.hour_intake import (
    Source,
    SourceHourIntake,
    healing_days,
    settled_missing_hours,
)
from bppanalyzer.locking import DirectoryLock, LockOwnershipLost, MaximumRunTimeExceeded
from bppanalyzer.object_store import ObjectStore
from bppanalyzer.operational_evidence import (
    CurrentRun,
    OperationalEvidence,
    RunEvidence,
    RunSummary,
)
from bppanalyzer.publication import (
    PRODUCTS,
    AnalysisWindow,
    BuiltSnapshot,
    ConsumerProduct,
    LatestPublisher,
    SnapshotBuilder,
    select_analysis_window,
)


class PipelineDriver:
    """Converge local facts, then build and replace each consumer object independently."""

    def __init__(
        self,
        data_root: str | Path,
        *,
        source: Source,
        source_epoch: date | str | None = None,
        clock: Callable[[], datetime] = lambda: datetime.now(UTC),
        max_run_seconds: float = 21600,
        duckdb_memory_limit: str,
        duckdb_threads: int,
        fact_retention_days: int = MIN_FACT_RETENTION_DAYS,
        object_store: ObjectStore | None = None,
    ) -> None:
        self.data_root = Path(data_root)
        self.source = source
        self.source_epoch = parse_source_day(source_epoch) if source_epoch is not None else None
        self.clock = clock
        self.max_run_seconds = max_run_seconds
        self.duckdb_memory_limit = duckdb_memory_limit
        self.duckdb_threads = duckdb_threads
        self.fact_retention_days = fact_retention_days
        self.object_store = object_store

    def run(
        self,
        *,
        heal_days: int,
        anchor_day: date | str | None = None,
        publish: bool = True,
        progress_callback: Callable[[str], None] | None = None,
        error_callback: Callable[[str], None] | None = None,
    ) -> RunSummary:
        parsed_anchor = parse_source_day(anchor_day) if anchor_day is not None else None
        now = aware_utc(self.clock())
        run_id = uuid.uuid4().hex
        lock = DirectoryLock(self.data_root, run_id, max_run_seconds=self.max_run_seconds)
        with lock:
            started_monotonic = time.monotonic()
            progress = RunEvidence(run_id, now, started_monotonic)
            _reset_source_performance(self.source)
            store = FactStore(self.data_root, clock=self.clock, ownership_check=lock.assert_owned)
            evidence = OperationalEvidence(
                self.data_root,
                run_id,
                clock=self.clock,
                ownership_check=lock.assert_owned,
            )
            evidence.log("run started")
            if lock.stale_run_id is not None:
                evidence.log(f"stale lock taken over: {lock.stale_run_id}")

            def report(message: str) -> None:
                evidence.log(message)
                if progress_callback is not None:
                    progress_callback(message)

            def report_error(message: str) -> None:
                evidence.log(message)
                if error_callback is not None:
                    error_callback(message)

            def checkpoint(
                phase: str,
                current_hour: str | None,
                *,
                step: str,
                bundles_done: int | None = None,
                bundles_total: int | None = None,
            ) -> None:
                try:
                    evidence.checkpoint(
                        store,
                        CurrentRun(
                            phase=phase,
                            step=step,
                            current_hour=current_hour,
                            hours_done=progress.hours_ingested,
                            hours_planned=progress.hours_planned,
                            bundles_done=bundles_done,
                            bundles_total=bundles_total,
                            started_at=now,
                            updated_at=aware_utc(self.clock()),
                        ),
                        considered_days=healing_days(
                            now, heal_days, source_epoch=self.source_epoch
                        ),
                        source_epoch=self.source_epoch,
                    )
                except LockOwnershipLost:
                    raise
                except BaseException as error:
                    evidence.try_log(f"live status refresh failed: {_error_reason(error)}")

            pending_error: BaseException | None = None
            pending_traceback = None

            def record_completion(log: Callable[[str], None]) -> None:
                source_snapshot = getattr(self.source, "performance_snapshot", None)
                progress.prepare_completion(
                    finished_monotonic=time.monotonic(),
                    source_performance=source_snapshot() if callable(source_snapshot) else None,
                )
                log(
                    "performance report: "
                    + json.dumps(
                        {
                            "downloads": progress.report.value["downloads"],
                            "timings": progress.timings,
                        },
                        sort_keys=True,
                        separators=(",", ":"),
                    )
                )
                log(
                    "run report: "
                    + json.dumps(progress.report.value, sort_keys=True, separators=(",", ":"))
                )

            try:
                self._heal(
                    store,
                    lock,
                    now=now,
                    heal_days=heal_days,
                    progress=progress,
                    log=evidence.log,
                    report=report,
                    report_error=report_error,
                    checkpoint=checkpoint,
                )
                progress.build_started_monotonic = time.monotonic()
                window = select_analysis_window(
                    store.seals(), parsed_anchor, source_epoch=self.source_epoch
                )
                self._publish_products(
                    store,
                    window,
                    publish=publish,
                    progress=progress,
                    lock=lock,
                    report=report,
                    report_error=report_error,
                    checkpoint=checkpoint,
                )
                if progress.report.all_published():
                    pruned = store.prune(retain_days=self.fact_retention_days)
                    progress.report.record_retention(pruned)
                    report(
                        "fact retention done: "
                        f"source_days={len(pruned.source_days)} "
                        f"hours={pruned.hours_pruned} files={pruned.files_pruned} "
                        f"bytes={pruned.bytes_pruned}"
                    )
                record_completion(evidence.log)
                evidence.log(f"run finished: {progress.outcome}")
            except BaseException as error:
                if isinstance(error, LockOwnershipLost) and not isinstance(
                    error, MaximumRunTimeExceeded
                ):
                    raise
                pending_error = error
                pending_traceback = error.__traceback__
                record_completion(evidence.try_log)
                evidence.try_log(f"run failed: {_error_reason(error)}")

            summary = progress.summarize(
                finished_at=aware_utc(self.clock()),
                fatal_reason=_error_reason(pending_error) if pending_error is not None else None,
            )

            ownership_check = (
                lock.assert_current_owner
                if isinstance(pending_error, MaximumRunTimeExceeded)
                else lock.assert_owned
            )
            try:
                ownership_check()
                evidence.finish(
                    store,
                    summary,
                    now=aware_utc(self.clock()),
                    considered_days=healing_days(now, heal_days, source_epoch=self.source_epoch),
                    source_epoch=self.source_epoch,
                    ownership_check=ownership_check,
                )
            except LockOwnershipLost:
                if pending_error is not None:
                    # Surface the original run failure, chained to the lost lock.
                    raise pending_error.with_traceback(pending_traceback)  # noqa: B904
                raise
            if pending_error is not None:
                raise pending_error.with_traceback(pending_traceback)
            return summary

    def _publish_products(
        self,
        store: FactStore,
        window: AnalysisWindow | None,
        *,
        publish: bool,
        progress: RunEvidence,
        lock: DirectoryLock,
        report: Callable[[str], None],
        report_error: Callable[[str], None],
        checkpoint: Callable[..., None],
    ) -> None:
        result = progress.report
        result.select_window(window)
        if window is None:
            report("publication skipped: no Complete Source Day available for Analysis Window")
            return
        builder = SnapshotBuilder(
            self.data_root,
            store=store,
            clock=self.clock,
            memory_limit=self.duckdb_memory_limit,
            threads=self.duckdb_threads,
        )
        fact_stats_started = time.monotonic()
        try:
            facts = builder.fact_stats(window)
        except Exception as error:
            report_error(f"fact report failed: {_error_reason(error)}")
        else:
            result.record_facts(facts)
        finally:
            progress.product_timings["fact_stats_seconds"] = round(
                max(time.monotonic() - fact_stats_started, 0.0), 6
            )
        publisher = LatestPublisher(self.object_store) if self.object_store is not None else None
        for consumer in PRODUCTS:
            product = consumer.name
            product_started: float | None = None
            try:
                checkpoint("build", None, step=product)
                report(f"{product} snapshot build started")
                product_started = time.monotonic()
                built = consumer.build(builder, window)
                progress.product_timings[f"{product}_build_seconds"] = round(
                    max(time.monotonic() - product_started, 0.0), 6
                )
                lock.assert_owned()
                save_started = time.monotonic()
                _write_local_snapshot(self.data_root, consumer, built, lock.assert_owned)
                progress.product_timings[f"{product}_local_save_seconds"] = round(
                    max(time.monotonic() - save_started, 0.0), 6
                )
                result.record_product(consumer, built.stats)
                report(f"{product} snapshot build done")
                if publish and publisher is not None:
                    checkpoint("publish", None, step=product)
                    publish_started = time.monotonic()
                    replaced = publisher.replace(built)
                    progress.product_timings[f"{product}_publish_seconds"] = round(
                        max(time.monotonic() - publish_started, 0.0), 6
                    )
                    result.mark_published(consumer)
                    report(f"{product} snapshot publication done: replaced={str(replaced).lower()}")
                elif not publish:
                    report(f"{product} snapshot publication skipped: --no-publish")
                else:
                    report(f"{product} snapshot publication skipped: no object store")
            except Exception as error:
                if product_started is not None:
                    progress.product_timings.setdefault(
                        f"{product}_build_seconds",
                        round(max(time.monotonic() - product_started, 0.0), 6),
                    )
                failure = {"scope": product, "reason": _error_reason(error)}
                progress.failures.append(failure)
                report_error(f"{product} snapshot failed: {failure['reason']}")

    def _heal(
        self,
        store: FactStore,
        lock: DirectoryLock,
        *,
        now: datetime,
        heal_days: int,
        progress: RunEvidence,
        log: Callable[[str], None],
        report: Callable[[str], None],
        report_error: Callable[[str], None],
        checkpoint: Callable[..., None],
    ) -> None:
        heal_started = time.monotonic()
        progress.heal_started_monotonic = heal_started
        intake = SourceHourIntake(self.source, store)
        observe_intake = partial(progress.observe_intake, report=report, checkpoint=checkpoint)
        days = healing_days(now, heal_days, source_epoch=self.source_epoch)
        planned_by_day: dict[date, tuple[datetime, ...]] = {}
        for day in days:
            if store.has_seal(day) or store.is_abandoned(day):
                continue
            planned_by_day[day] = settled_missing_hours(day, now, store.missing_hours(day))
        progress.hours_planned = sum(len(hours) for hours in planned_by_day.values())
        report(f"heal plan: days={len(days)} missing_settled_hours={progress.hours_planned}")
        hours_started = 0
        for day, planned_hours in planned_by_day.items():
            lock.assert_owned()
            day_started = time.monotonic()
            expired_reason: str | None = None
            for hour in planned_hours:
                lock.assert_owned()
                hours_started += 1
                hour_key = hour.strftime("%Y-%m-%dT%H")
                report(
                    f"hour started: source_hour={hour_key} "
                    f"[{hours_started}/{progress.hours_planned}]"
                )
                result = intake.commit(hour, on_progress=observe_intake)
                if isinstance(error := result.error, HourExpired):
                    expired_reason = error.reason
                    report_error(f"source hour expired: {hour_key} ({error.reason})")
                    break
                if isinstance(error, RetryableSourceError):
                    progress.failures.append(
                        {
                            "scope": "source_hour",
                            "source_hour": hour_key,
                            "reason": error.reason,
                        }
                    )
                    report_error(f"source hour failed: {hour_key} ({error.reason})")
            if expired_reason is not None:
                remaining = store.missing_hours(day)
                store.abandon_day(day, remaining, expired_reason)
                log(f"source day abandoned: {day.isoformat()} ({expired_reason})")
                progress.days_abandoned += 1
                progress.changed = True
                checkpoint("heal", hour.strftime("%Y-%m-%dT%H"), step="abandoned")
                report(
                    f"abandoned {day.isoformat()} missing={len(remaining)} "
                    f"reason={expired_reason} "
                    f"elapsed={_format_elapsed(time.monotonic() - day_started)}"
                )
                continue
            try:
                if not store.missing_hours(day):
                    seal = store.seal_day(day)
                    progress.days_sealed += 1
                    progress.changed = True
                    checkpoint("heal", f"{day.isoformat()}T23", step="seal")
                    report(
                        f"sealed {day.isoformat()} rows={sum(seal.row_counts.values())} "
                        f"elapsed={_format_elapsed(time.monotonic() - day_started)}"
                    )
            except Exception as error:
                progress.failures.append(
                    {
                        "scope": "source_day",
                        "source_day": day.isoformat(),
                        "reason": _error_reason(error),
                    }
                )
                report_error(f"source day failed: {day.isoformat()} ({_error_reason(error)})")
        progress.heal_seconds = time.monotonic() - heal_started


def _reset_source_performance(source: Source) -> None:
    snapshot = getattr(source, "performance_snapshot", None)
    if callable(snapshot):
        snapshot(reset=True)


def _write_local_snapshot(
    root: Path,
    product: ConsumerProduct,
    snapshot: BuiltSnapshot,
    ownership_check: Callable[[], None],
) -> None:
    atomic_replace(
        root / product.local_relpath,
        snapshot.content,
        ownership_check=ownership_check,
    )


def _error_reason(error: BaseException) -> str:
    reason = getattr(error, "reason", None)
    if isinstance(reason, str) and reason:
        return reason
    return str(error) or type(error).__name__


def _format_elapsed(value: float) -> str:
    return f"{max(round(value), 0)}s"
