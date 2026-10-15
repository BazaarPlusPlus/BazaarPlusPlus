"""Commit one complete Source Hour behind one transaction interface."""

import time
from collections.abc import Callable, Iterator
from dataclasses import dataclass, replace
from datetime import date, datetime, timedelta
from typing import Literal, Protocol

from bppanalyzer.bundle_source import Bundle, HourExpired, RawHourIndex, RetryableSourceError
from bppanalyzer.durable import aware_utc
from bppanalyzer.fact_store import FactStore, HourCommit, parse_source_day
from bppanalyzer.projection import project_hour

DEFAULT_SETTLE_LAG = timedelta(seconds=60)


class Source(Protocol):
    def hour_index(self, source_hour: datetime) -> RawHourIndex: ...

    def stream(self, index: RawHourIndex) -> Iterator[Bundle]: ...


@dataclass(frozen=True, slots=True)
class HourIntakeProgress:
    """Cumulative facts for one attempt; commit or error marks its terminal observation.

    Unknown index size is None, distinct from an empty hour. Succeeded Bundles
    passed source admission, not necessarily projection or durable commit.
    """

    source_hour: str
    step: Literal["index", "ingest", "complete"] = "index"
    expected_bundles: int | None = None
    succeeded_bundles: int = 0
    failed_bundles: int = 0
    listing_pages: int = 0
    source_index_seconds: float = 0.0
    source_ingest_seconds: float = 0.0
    elapsed_seconds: float = 0.0
    commit: HourCommit | None = None
    error: BaseException | None = None


BUNDLE_PROGRESS_EVERY = 250


class SourceHourIntake:
    """Own enumeration, admission, bounded projection, and immutable fact commit."""

    def __init__(
        self,
        source: Source,
        store: FactStore,
        *,
        monotonic: Callable[[], float] = time.monotonic,
    ) -> None:
        self._source = source
        self._store = store
        self._monotonic = monotonic

    def commit(
        self,
        source_hour: datetime,
        *,
        on_progress: Callable[[HourIntakeProgress], None] = lambda _progress: None,
    ) -> HourIntakeProgress:
        """Return a committed or recoverable source result, with live observations.

        Fatal errors propagate unchanged. Only source expiry and retryable source
        errors return a failed result; the caller owns retry and abandonment.

        Observers run synchronously before the next Bundle is projected. An
        observer failure aborts the attempt, but is not a download failure.
        Failure observation must not mask the original error. Success is already
        durable when observed, so its observer cannot turn it into intake failure.
        """
        started = self._monotonic()
        index_started = started
        ingest_started: float | None = None
        progress = HourIntakeProgress(aware_utc(source_hour).strftime("%Y-%m-%dT%H"))
        completed = 0
        observer_error: BaseException | None = None

        def observe(update: HourIntakeProgress) -> None:
            nonlocal observer_error
            try:
                on_progress(update)
            except BaseException as error:
                observer_error = error
                raise

        try:
            observe(progress)
            index_started = self._monotonic()
            index = self._source.hour_index(source_hour)
            total = len(index.items)
            indexed_at = self._monotonic()
            progress = replace(
                progress,
                step="ingest",
                expected_bundles=total,
                listing_pages=index.pages,
                source_index_seconds=indexed_at - index_started,
                elapsed_seconds=indexed_at - started,
            )
            observe(progress)
            ingest_started = self._monotonic()

            def admitted_bundles() -> Iterator[Bundle]:
                nonlocal completed
                for completed, bundle in enumerate(self._source.stream(index), start=1):
                    if (
                        completed == 1
                        or completed == total
                        or completed % BUNDLE_PROGRESS_EVERY == 0
                    ):
                        observed_at = self._monotonic()
                        observe(
                            replace(
                                progress,
                                succeeded_bundles=completed,
                                source_ingest_seconds=observed_at - ingest_started,
                                elapsed_seconds=observed_at - started,
                            )
                        )
                    yield bundle

            commit = self._store.commit_hour(project_hour(index, admitted_bundles()))
        except BaseException as error:
            failed_at = self._monotonic()
            failed = replace(
                progress,
                succeeded_bundles=completed,
                failed_bundles=(
                    max((progress.expected_bundles or 0) - completed, 0)
                    if isinstance(error, RetryableSourceError) and error is not observer_error
                    else 0
                ),
                source_index_seconds=(
                    failed_at - index_started
                    if progress.expected_bundles is None
                    else progress.source_index_seconds
                ),
                source_ingest_seconds=(
                    failed_at - ingest_started if ingest_started is not None else 0.0
                ),
                elapsed_seconds=failed_at - started,
                error=error,
            )
            try:
                on_progress(failed)
            except BaseException as observation_error:
                raise error from observation_error
            if error is not observer_error and isinstance(
                error, (HourExpired, RetryableSourceError)
            ):
                return failed
            raise

        finished_at = self._monotonic()
        result = replace(
            progress,
            step="complete",
            succeeded_bundles=completed,
            source_ingest_seconds=finished_at - ingest_started,
            elapsed_seconds=finished_at - started,
            commit=commit,
        )
        on_progress(result)
        return result


def healing_days(
    now: datetime, count: int, *, source_epoch: date | str | None = None
) -> tuple[date, ...]:
    """Return the oldest-first UTC Source Days considered by an invocation."""
    current = aware_utc(now).date()
    first = current - timedelta(days=count - 1)
    epoch = parse_source_day(source_epoch) if source_epoch is not None else None
    return tuple(
        day
        for offset in range(count)
        if (day := first + timedelta(days=offset)) >= (epoch or first)
    )


def is_hour_settled(
    source_hour: datetime,
    now: datetime,
    *,
    settle_lag: timedelta = DEFAULT_SETTLE_LAG,
) -> bool:
    hour = aware_utc(source_hour)
    if hour.minute or hour.second or hour.microsecond:
        raise ValueError("Source Hour must align to the hour")
    return aware_utc(now) >= hour + timedelta(hours=1) + settle_lag


def settled_missing_hours(
    source_day: date,
    now: datetime,
    missing: tuple[datetime, ...],
    *,
    settle_lag: timedelta = DEFAULT_SETTLE_LAG,
) -> tuple[datetime, ...]:
    if any(hour.date() != source_day for hour in missing):
        raise ValueError("Missing Source Hours must belong to the Source Day")
    return tuple(
        sorted(hour for hour in missing if is_hour_settled(hour, now, settle_lag=settle_lag))
    )
