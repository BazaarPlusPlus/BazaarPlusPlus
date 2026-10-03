from datetime import UTC, datetime
from pathlib import Path

import pytest

from bppanalyzer.bundle_source import (
    BundleRef,
    RawHourIndex,
    RetryableSourceError,
    admit_bundle,
    raw_commit_sha256,
)
from bppanalyzer.fact_store import FactStore
from bppanalyzer.hour_intake import SourceHourIntake
from tests.bundle_fixtures import bundle_bytes

SOURCE_HOUR = datetime(2026, 8, 10, 12, tzinfo=UTC)


class FixtureSource:
    def __init__(self, *, fail_after_first: bool = False) -> None:
        self.fail_after_first = fail_after_first

    def hour_index(self, source_hour: datetime) -> RawHourIndex:
        timestamp = int(source_hour.timestamp() * 1_000)
        refs = tuple(
            BundleRef(
                bundle_id=f"bundle-{number}",
                available_at_ms=timestamp + number,
                download_url=f"https://download.invalid/bundle-{number}",
                download_expires_at_ms=timestamp + 60_000,
            )
            for number in range(2)
        )
        return RawHourIndex(source_hour, refs, raw_commit_sha256(refs), 1)

    def stream(self, index: RawHourIndex):
        for number, ref in enumerate(index.items):
            if number == 1 and self.fail_after_first:
                raise RetryableSourceError("fixture_failed", "fixture stream failed")
            yield admit_bundle(ref, bundle_bytes(ref.bundle_id))


def test_source_hour_intake_never_commits_a_partial_stream(tmp_path: Path) -> None:
    with pytest.raises(RetryableSourceError, match="fixture stream failed"):
        SourceHourIntake(FixtureSource(fail_after_first=True), FactStore(tmp_path)).commit(
            SOURCE_HOUR
        )

    assert not (tmp_path / "facts/hourly/source_hour=2026-08-10T12").exists()
