from datetime import UTC, datetime
from pathlib import Path

import pytest

from bppanalyzer.bundle_source import (
    BundleRef,
    HourExpired,
    RawHourIndex,
    RetryableSourceError,
    admit_bundle,
    raw_commit_sha256,
)
from bppanalyzer.fact_store import FactStore
from bppanalyzer.hour_intake import HourIntakeProgress, SourceHourIntake
from bppanalyzer.locking import LockOwnershipLost, MaximumRunTimeExceeded
from tests.bundle_fixtures import bundle_bytes

SOURCE_HOUR = datetime(2026, 8, 10, 12, tzinfo=UTC)


class Clock:
    def __init__(self) -> None:
        self.seconds = 0.0

    def __call__(self) -> float:
        return self.seconds

    def advance(self, seconds: float) -> None:
        self.seconds += seconds


class FixtureSource:
    def __init__(
        self,
        clock: Clock,
        *,
        count: int = 2,
        index_error: BaseException | None = None,
        stream_error: BaseException | None = None,
    ) -> None:
        self.clock = clock
        self.count = count
        self.index_error = index_error
        self.stream_error = stream_error
        self.exhausted = False

    def hour_index(self, source_hour: datetime) -> RawHourIndex:
        self.exhausted = False
        self.clock.advance(2)
        if self.index_error is not None:
            raise self.index_error
        timestamp = int(source_hour.timestamp() * 1_000)
        refs = tuple(
            BundleRef(
                bundle_id=f"bundle-{number}",
                available_at_ms=timestamp + number,
                download_url=f"https://download.invalid/bundle-{number}",
                download_expires_at_ms=timestamp + 60_000,
            )
            for number in range(self.count)
        )
        return RawHourIndex(source_hour, refs, raw_commit_sha256(refs), 1)

    def stream(self, index: RawHourIndex):
        for number, ref in enumerate(index.items):
            self.clock.advance(3)
            if number == 1 and self.stream_error is not None:
                raise self.stream_error
            yield admit_bundle(ref, bundle_bytes(ref.bundle_id))
        self.exhausted = True


def _failure(kind: str) -> BaseException:
    if kind == "retryable":
        return RetryableSourceError("fixture_failed", "fixture stream failed")
    if kind == "expired":
        return HourExpired("source_hour_expired", "fixture expired")
    if kind == "lock":
        return LockOwnershipLost("fixture ownership lost")
    if kind == "deadline":
        return MaximumRunTimeExceeded("fixture deadline reached")
    if kind == "interrupt":
        return KeyboardInterrupt("fixture interrupt")
    return RuntimeError("fixture failure")


@pytest.mark.parametrize("kind", ["retryable", "expired", "lock", "deadline", "interrupt", "fatal"])
def test_index_failure_reports_unknown_total_and_preserves_original_error(
    tmp_path: Path, kind: str
) -> None:
    clock = Clock()
    error = _failure(kind)
    updates: list[HourIntakeProgress] = []
    store = FactStore(tmp_path)
    intake = SourceHourIntake(FixtureSource(clock, index_error=error), store, monotonic=clock)

    if kind in {"retryable", "expired"}:
        result = intake.commit(SOURCE_HOUR, on_progress=updates.append)
        assert result is updates[-1]
        assert result.error is error
    else:
        with pytest.raises(type(error)) as raised:
            intake.commit(SOURCE_HOUR, on_progress=updates.append)
        assert raised.value is error
    assert len(updates) == 2
    assert updates[0] == HourIntakeProgress("2026-08-10T12")
    assert updates[-1] == HourIntakeProgress(
        "2026-08-10T12", source_index_seconds=2, elapsed_seconds=2, error=error
    )
    assert store.committed_hours() == ()


@pytest.mark.parametrize("kind", ["retryable", "expired", "lock", "deadline", "interrupt", "fatal"])
def test_partial_stream_reports_admission_counts_and_never_commits(
    tmp_path: Path, kind: str
) -> None:
    clock = Clock()
    error = _failure(kind)
    updates: list[HourIntakeProgress] = []
    store = FactStore(tmp_path)
    intake = SourceHourIntake(FixtureSource(clock, stream_error=error), store, monotonic=clock)

    if kind in {"retryable", "expired"}:
        result = intake.commit(SOURCE_HOUR, on_progress=updates.append)
        assert result is updates[-1]
        assert result.error is error
    else:
        with pytest.raises(type(error)) as raised:
            intake.commit(SOURCE_HOUR, on_progress=updates.append)
        assert raised.value is error
    assert [(u.step, u.succeeded_bundles) for u in updates] == [
        ("index", 0),
        ("ingest", 0),
        ("ingest", 1),
        ("ingest", 1),
    ]
    assert updates[-1] == HourIntakeProgress(
        "2026-08-10T12",
        step="ingest",
        expected_bundles=2,
        succeeded_bundles=1,
        failed_bundles=1 if kind == "retryable" else 0,
        listing_pages=1,
        source_index_seconds=2,
        source_ingest_seconds=6,
        elapsed_seconds=8,
        error=error,
    )
    assert store.committed_hours() == ()


@pytest.mark.parametrize("failure_at", ["write_fence", "finalize"])
def test_commit_failure_preserves_download_success_without_claiming_a_commit(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch, failure_at: str
) -> None:
    clock = Clock()
    source = FixtureSource(clock)
    error = (
        LockOwnershipLost("fixture commit fenced")
        if failure_at == "write_fence"
        else OSError("fixture atomic rename failed")
    )
    updates: list[HourIntakeProgress] = []

    def ownership_check() -> None:
        if source.exhausted and failure_at == "write_fence":
            clock.advance(5)
            raise error

    def fail_rename(_source, _destination) -> None:
        clock.advance(5)
        raise error

    if failure_at == "finalize":
        monkeypatch.setattr("bppanalyzer.fact_store.os.rename", fail_rename)

    store = FactStore(tmp_path, ownership_check=ownership_check)
    with pytest.raises(type(error)) as raised:
        SourceHourIntake(source, store, monotonic=clock).commit(
            SOURCE_HOUR, on_progress=updates.append
        )

    assert raised.value is error
    assert source.exhausted
    assert updates[-1] == HourIntakeProgress(
        "2026-08-10T12",
        step="ingest",
        expected_bundles=2,
        succeeded_bundles=2,
        listing_pages=1,
        source_index_seconds=2,
        source_ingest_seconds=11,
        elapsed_seconds=13,
        error=error,
    )
    assert store.committed_hours() == ()


@pytest.mark.parametrize("count", [0, 1, 501])
def test_live_progress_is_bounded_and_precedes_commit(tmp_path: Path, count: int) -> None:
    clock = Clock()
    source = FixtureSource(clock, count=count)
    store = FactStore(tmp_path)
    updates: list[HourIntakeProgress] = []

    def observe(update: HourIntakeProgress) -> None:
        updates.append(update)
        if update.commit is None:
            assert not source.exhausted
            assert store.committed_hours() == ()
        else:
            assert source.exhausted
            assert store.committed_hours() == ("2026-08-10T12",)

    result = SourceHourIntake(source, store, monotonic=clock).commit(
        SOURCE_HOUR, on_progress=observe
    )
    assert result is updates[-1]
    commit = result.commit
    assert commit is not None

    progress_counts = [1, 250, 500, 501] if count == 501 else ([1] if count else [])
    assert [(u.step, u.succeeded_bundles) for u in updates] == [
        ("index", 0),
        ("ingest", 0),
        *[("ingest", completed) for completed in progress_counts],
        ("complete", count),
    ]
    assert updates[-1] == HourIntakeProgress(
        "2026-08-10T12",
        step="complete",
        expected_bundles=count,
        succeeded_bundles=count,
        listing_pages=1,
        source_index_seconds=2,
        source_ingest_seconds=3 * count,
        elapsed_seconds=2 + 3 * count,
        commit=commit,
    )
    assert commit.bundle_count == count
    assert not commit.reused
    assert store.has_hour(SOURCE_HOUR)


def test_reused_commit_and_each_attempt_keep_their_own_counts(tmp_path: Path) -> None:
    clock = Clock()
    intake = SourceHourIntake(FixtureSource(clock), FactStore(tmp_path), monotonic=clock)
    updates: list[HourIntakeProgress] = []
    first = intake.commit(SOURCE_HOUR, on_progress=updates.append)
    second = intake.commit(SOURCE_HOUR, on_progress=updates.append)

    assert first.commit is not None and second.commit is not None
    assert first.commit.fact_commit_sha256 == second.commit.fact_commit_sha256
    assert second.commit.reused
    results = [u for u in updates if u.commit is not None]
    assert [u.expected_bundles for u in results] == [2, 2]
    assert [u.succeeded_bundles for u in results] == [2, 2]
    assert [u.source_index_seconds for u in results] == [2, 2]
    assert [u.source_ingest_seconds for u in results] == [6, 6]


@pytest.mark.parametrize("kind", ["retryable", "lock", "deadline", "interrupt", "fatal"])
def test_progress_observer_failure_aborts_without_inventing_download_failures(
    tmp_path: Path, kind: str
) -> None:
    clock = Clock()
    error = _failure(kind)
    updates: list[HourIntakeProgress] = []
    store = FactStore(tmp_path)

    def observe(update: HourIntakeProgress) -> None:
        updates.append(update)
        if update.succeeded_bundles == 1 and update.error is None:
            raise error

    with pytest.raises(type(error)) as raised:
        SourceHourIntake(FixtureSource(clock), store, monotonic=clock).commit(
            SOURCE_HOUR, on_progress=observe
        )

    assert raised.value is error
    assert updates[-1].error is error
    assert updates[-1].succeeded_bundles == 1
    assert updates[-1].failed_bundles == 0
    assert updates[-1].source_ingest_seconds == 3
    assert store.committed_hours() == ()


@pytest.mark.parametrize("kind", ["retryable", "lock", "deadline", "interrupt"])
def test_failure_observer_cannot_mask_original_failure(tmp_path: Path, kind: str) -> None:
    clock = Clock()
    error = _failure(kind)
    secondary = OSError("fixture evidence failure")

    def observe(update: HourIntakeProgress) -> None:
        if update.error is not None:
            raise secondary

    with pytest.raises(type(error)) as raised:
        SourceHourIntake(
            FixtureSource(clock, stream_error=error), FactStore(tmp_path), monotonic=clock
        ).commit(SOURCE_HOUR, on_progress=observe)

    assert raised.value is error
    assert raised.value.__cause__ is secondary


def test_success_observer_failure_cannot_turn_a_committed_hour_into_failure(tmp_path: Path) -> None:
    clock = Clock()
    store = FactStore(tmp_path)
    updates: list[HourIntakeProgress] = []
    error = OSError("fixture completed evidence failure")

    def observe(update: HourIntakeProgress) -> None:
        updates.append(update)
        if update.commit is not None:
            raise error

    with pytest.raises(OSError) as raised:
        SourceHourIntake(FixtureSource(clock), store, monotonic=clock).commit(
            SOURCE_HOUR, on_progress=observe
        )

    assert raised.value is error
    assert updates[-1].commit is not None
    assert all(u.error is None for u in updates)
    assert store.has_hour(SOURCE_HOUR)
