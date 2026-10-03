import hashlib
import json
from datetime import UTC, datetime
from pathlib import Path

import httpx
import pytest

from bppanalyzer.bundle_source import (
    BundleRef,
    BundleSource,
    RawHourIndex,
    RetryableSourceError,
    SourceContractError,
    admit_bundle,
    raw_commit_sha256,
)
from bppanalyzer.projection import project_hour
from tests.fakes import collect_tables
from tests.pipeline_fixtures import (
    PROJECTION_CASES,
    PROJECTION_GOLDENS,
    assert_golden,
    mod_run_payload_bundle,
    read_base64,
)
from tests.pipeline_fixtures import SERVER_FIXTURES as BUNDLE_FIXTURES

EXPECTED_PROJECTION = Path(__file__).parent / "fixtures/run-payload-v5.projection.json"
SOURCE_HOUR = datetime(2026, 8, 3, 1, tzinfo=UTC)


def _reference(content: bytes, bundle_id: str) -> BundleRef:
    available_at_ms = int(SOURCE_HOUR.timestamp() * 1_000)
    return BundleRef(
        bundle_id=bundle_id,
        available_at_ms=available_at_ms,
        download_url=f"https://download.invalid/{bundle_id}",
        download_expires_at_ms=available_at_ms + 60_000,
    )


def _index(ref: BundleRef) -> RawHourIndex:
    return RawHourIndex(
        source_hour=SOURCE_HOUR,
        items=(ref,),
        raw_commit_sha256=raw_commit_sha256((ref,)),
        pages=1,
    )


def test_server_run_only_golden_matches_manifest_and_checksums() -> None:
    content = read_base64(BUNDLE_FIXTURES / "run-only.bundle.b64")
    expected_manifest = json.loads((BUNDLE_FIXTURES / "run-only.manifest.json").read_text())
    checksums = json.loads((BUNDLE_FIXTURES / "checksums.json").read_text())["run-only.bundle.b64"]

    bundle = admit_bundle(_reference(content, expected_manifest["bundle_id"]), content)

    assert bundle.manifest == expected_manifest
    assert bundle.bytes == checksums["decoded_bytes"]
    assert bundle.sha256 == checksums["sha256"]
    manifest_bytes = int.from_bytes(content[12:16], "big")
    assert manifest_bytes == checksums["manifest_bytes"]
    payload = expected_manifest["run"]["payload"]
    assert len(bundle.run_content) == payload["length"]
    assert hashlib.sha256(bundle.run_content).hexdigest() == payload["sha256"]
    assert bundle.run_content == content[16 + manifest_bytes + payload["offset"] :]


@pytest.mark.parametrize(
    "filename", ["corrupt-magic.bundle.b64", "segment-digest-mismatch.bundle.b64"]
)
def test_server_corrupt_goldens_report_analyzer_validation_errors(filename: str) -> None:
    content = read_base64(BUNDLE_FIXTURES / filename)
    manifest = json.loads((BUNDLE_FIXTURES / "run-only.manifest.json").read_text())
    checksums = json.loads((BUNDLE_FIXTURES / "checksums.json").read_text())[filename]
    reason = checksums.get("expected_reason", checksums["expected_error"])
    ref = _reference(content, manifest["bundle_id"])
    if "sha256" in checksums:
        assert hashlib.sha256(content).hexdigest() == checksums["sha256"]

    with pytest.raises(SourceContractError) as rejected:
        admit_bundle(ref, content)
    assert rejected.value.reason == reason

    with (
        httpx.Client(
            transport=httpx.MockTransport(lambda _request: httpx.Response(200, content=content))
        ) as client,
        BundleSource(
            api_base_url="https://api.invalid",
            sync_token="test-token",
            client=client,
            clock=lambda: SOURCE_HOUR,
        ) as source,
        pytest.raises(RetryableSourceError) as streamed,
    ):
        list(source.stream(_index(ref)))

    assert streamed.value.reason == "bundle_validation_failed"
    assert str(streamed.value) == f"Bundle validation failed: {reason}"
    assert isinstance(streamed.value.__cause__, SourceContractError)
    assert streamed.value.__cause__.reason == reason


def test_mod_run_payload_golden_projects_expected_tables() -> None:
    created_at_ms = int(SOURCE_HOUR.timestamp() * 1_000)
    served = mod_run_payload_bundle(created_at_ms, created_at_ms=created_at_ms)
    ref = _reference(served.content, served.bundle_id)

    projection = project_hour(_index(ref), [admit_bundle(ref, served.content)])

    expected = json.loads(EXPECTED_PROJECTION.read_text(encoding="utf-8"))
    tables = collect_tables(projection)
    assert {name: table.to_pylist() for name, table in tables.items()} == expected
    assert projection.bundle_count == 1


@pytest.mark.parametrize("case", sorted(PROJECTION_CASES))
def test_projection_rule_case_matches_its_golden_tables(case: str) -> None:
    served = PROJECTION_CASES[case]
    ref = BundleRef(
        bundle_id=served.bundle_id,
        available_at_ms=served.available_at_ms,
        download_url=f"https://download.invalid/{served.bundle_id}",
        download_expires_at_ms=served.available_at_ms + 60_000,
    )
    source_hour = datetime.fromtimestamp(served.available_at_ms // 3_600_000 * 3_600, tz=UTC)
    index = RawHourIndex(source_hour, (ref,), raw_commit_sha256((ref,)), 1)

    tables = collect_tables(project_hour(index, [admit_bundle(ref, served.content)]))

    projected = {name: table.to_pylist() for name, table in tables.items()}
    content = (json.dumps(projected, indent=2, ensure_ascii=False) + "\n").encode()
    assert_golden(PROJECTION_GOLDENS / f"{case}.json", content)
