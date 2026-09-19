"""Commit one complete Source Hour behind one transaction interface."""

from collections.abc import Callable, Iterator
from datetime import UTC, date, datetime, timedelta
from typing import Protocol

from bppanalyzer.bundle_source import Bundle, RawHourIndex
from bppanalyzer.fact_store import FactStore, HourCommit, parse_source_day
from bppanalyzer.projection import project_hour

DEFAULT_SETTLE_LAG = timedelta(seconds=60)


class Source(Protocol):
    def hour_index(self, source_hour: datetime) -> RawHourIndex: ...

    def stream(self, index: RawHourIndex) -> Iterator[Bundle]: ...


class SourceHourIntake:
    """Own enumeration, admission, bounded projection, and immutable fact commit."""

    def __init__(self, source: Source, store: FactStore) -> None:
        self._source = source
        self._store = store

    def commit(
        self,
        source_hour: datetime,
        *,
        on_indexed: Callable[[int, int], None] = lambda _bundles, _pages: None,
        on_bundle: Callable[[int, int], None] = lambda _completed, _total: None,
    ) -> HourCommit:
        index = self._source.hour_index(source_hour)
        total = len(index.items)
        on_indexed(total, index.pages)

        def admitted_bundles() -> Iterator[Bundle]:
            for completed, bundle in enumerate(self._source.stream(index), start=1):
                if not isinstance(bundle, Bundle):
                    raise TypeError("Source stream must yield admitted Bundle values")
                on_bundle(completed, total)
                yield bundle

        return self._store.commit_hour(project_hour(index, admitted_bundles()))


def healing_days(
    now: datetime, count: int, *, source_epoch: date | str | None = None
) -> tuple[date, ...]:
    """Return the oldest-first UTC Source Days considered by an invocation."""
    current = _aware_utc(now).date()
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
    hour = _aware_utc(source_hour)
    if hour.minute or hour.second or hour.microsecond:
        raise ValueError("Source Hour must align to the hour")
    return _aware_utc(now) >= hour + timedelta(hours=1) + settle_lag


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


def _aware_utc(value: datetime) -> datetime:
    if not isinstance(value, datetime) or value.tzinfo is None or value.utcoffset() is None:
        raise ValueError("Pipeline clock must be timezone-aware")
    return value.astimezone(UTC)
