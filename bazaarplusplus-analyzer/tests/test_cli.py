import json
from datetime import UTC, datetime
from functools import partial
from pathlib import Path

from click.testing import CliRunner

import bppanalyzer.cli as cli
from bppanalyzer.bundle_source import RetryableSourceError
from bppanalyzer.config import Config
from bppanalyzer.driver import PipelineDriver
from bppanalyzer.locking import DirectoryLock
from bppanalyzer.object_store import LocalObjectStore


class FailedContextSource:
    def __init__(self, **_kwargs) -> None:
        pass

    def __enter__(self):
        return self

    def __exit__(self, *_args) -> None:
        pass

    def hour_index(self, _source_hour):
        raise RetryableSourceError("fixture_retryable", "fixture failed")

    def stream(self, _index):
        raise AssertionError("A failed index must never be streamed")


def _config(root: Path) -> Config:
    return Config(root, "https://api.invalid", "test-token")


def test_cli_dry_run_has_no_external_or_local_side_effects(tmp_path: Path, monkeypatch) -> None:
    data_root = tmp_path / "data"
    monkeypatch.setattr(cli, "load_config", lambda **_kwargs: _config(data_root))

    result = CliRunner().invoke(cli.main, ["run", "--dry-run"])

    assert result.exit_code == 0
    report = json.loads(result.output.splitlines()[0])
    assert report["window"] is None
    assert report["heroes"]["published"] is False
    assert report["builds"]["published"] is False
    assert not data_root.exists()


def test_cli_preserves_usage_lock_and_partial_exit_codes(tmp_path: Path, monkeypatch) -> None:
    now = datetime(2026, 8, 7, 1, 1, tzinfo=UTC)
    monkeypatch.setattr(cli, "load_config", lambda **_kwargs: _config(tmp_path))
    monkeypatch.setattr(cli, "BundleSource", FailedContextSource)
    monkeypatch.setattr(cli, "PipelineDriver", partial(PipelineDriver, clock=lambda: now))
    monkeypatch.setattr(cli, "_object_store", lambda _config: LocalObjectStore(tmp_path / "r2"))
    runner = CliRunner()

    assert runner.invoke(cli.main, ["run", "--heal-days", "0"]).exit_code == 2
    holder = DirectoryLock(tmp_path, "held-by-test", heartbeat_interval=60)
    holder.acquire()
    try:
        assert runner.invoke(cli.main, ["run"]).exit_code == 3
    finally:
        holder.release()
    partial_result = runner.invoke(cli.main, ["run", "--heal-days", "1", "--quiet"])
    assert partial_result.exit_code == 4
    assert "source hour failed: 2026-08-07T00 (fixture_retryable)" in partial_result.output


def test_cli_exposes_only_current_pipeline_operator_commands() -> None:
    result = CliRunner().invoke(cli.main, ["--help"])

    assert result.exit_code == 0
    assert "run" in result.output
    assert "status" in result.output
    assert "verify" in result.output
    assert "rollback" not in result.output
    assert "release" not in result.output


def test_cli_status_text_shows_live_current_run_progress(tmp_path: Path, monkeypatch) -> None:
    status = {
        "facts": {"newest_sealed_day": None, "abandoned_days": []},
        "current_run": {
            "run_id": "live-run",
            "phase": "heal",
            "step": "ingest",
            "current_hour": "2026-08-07T00",
            "hours_done": 1,
            "hours_planned": 2,
        },
        "last_run": {"outcome": "ok"},
    }
    (tmp_path / "status.json").write_text(json.dumps(status))
    monkeypatch.setattr(cli, "load_config", lambda **_kwargs: Config(tmp_path, None, None))

    result = CliRunner().invoke(cli.main, ["status"])

    assert result.exit_code == 0
    assert (
        "current run: live-run phase=heal step=ingest hour=2026-08-07T00 hours=1/2" in result.output
    )
