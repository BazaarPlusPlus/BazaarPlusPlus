import base64
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

REPO_ROOT = Path(__file__).resolve().parents[2]
BUNDLE_FIXTURES = REPO_ROOT / "bazaarplusplus-server/contracts/v5/fixtures"
RUN_FIXTURE = (
    REPO_ROOT / "bazaarplusplus-mod/tests/BundleV5Codec.Tests/fixtures/run-payload-v5.fixture.b64"
)
EXPECTED_PROJECTION = Path(__file__).parent / "fixtures/run-payload-v5.projection.json"
SOURCE_HOUR = datetime(2026, 8, 3, 1, tzinfo=UTC)


def _read_base64(path: Path) -> bytes:
    return base64.b64decode(path.read_text(encoding="ascii").strip(), validate=True)


def _reference(content: bytes, bundle_id: str) -> BundleRef:
    available_at_ms = int(SOURCE_HOUR.timestamp() * 1_000)
    return BundleRef(
        bundle_id=bundle_id,
        available_at_ms=available_at_ms,
        download_url=f"https://download.invalid/{bundle_id}",
        download_expires_at_ms=available_at_ms + 60_000,
        sha256=hashlib.sha256(content).hexdigest(),
        bytes=len(content),
    )


def _index(ref: BundleRef) -> RawHourIndex:
    return RawHourIndex(
        source_hour=SOURCE_HOUR,
        items=(ref,),
        raw_commit_sha256=raw_commit_sha256((ref,)),
        pages=1,
    )


def test_server_run_only_golden_matches_manifest_and_checksums() -> None:
    content = _read_base64(BUNDLE_FIXTURES / "run-only.bundle.b64")
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
    content = _read_base64(BUNDLE_FIXTURES / filename)
    manifest = json.loads((BUNDLE_FIXTURES / "run-only.manifest.json").read_text())
    checksums = json.loads((BUNDLE_FIXTURES / "checksums.json").read_text())[filename]
    reason = checksums.get("expected_reason", checksums["expected_error"])
    ref = _reference(content, manifest["bundle_id"])
    if "sha256" in checksums:
        assert ref.sha256 == checksums["sha256"]

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
    # Only the envelope is assembled here; the C# writer owns the encoded Run bytes.
    run_content = _read_base64(RUN_FIXTURE)
    manifest = {
        "bundle_id": "01J00000000000000000000902",
        "bundle_version": 5,
        "created_at_ms": int(SOURCE_HOUR.timestamp() * 1_000),
        "run": {
            "run_id": "payload-run",
            "player_account_id": "golden-account",
            "run_format_version": 5,
            "projection": {"run": {}, "battles": []},
            "payload": {
                "offset": 0,
                "length": len(run_content),
                "sha256": hashlib.sha256(run_content).hexdigest(),
                "content_type": "application/x-bpp-run-v5",
            },
        },
    }
    encoded_manifest = json.dumps(manifest, sort_keys=True, separators=(",", ":")).encode()
    content = (
        b"BPPBNDL5"
        + (5).to_bytes(4, "big")
        + len(encoded_manifest).to_bytes(4, "big")
        + encoded_manifest
        + run_content
    )
    ref = _reference(content, manifest["bundle_id"])

    projection = project_hour(_index(ref), [admit_bundle(ref, content)])

    expected = json.loads(EXPECTED_PROJECTION.read_text(encoding="utf-8"))
    assert {name: table.to_pylist() for name, table in projection.tables.items()} == expected
    assert projection.bundle_count == 1
