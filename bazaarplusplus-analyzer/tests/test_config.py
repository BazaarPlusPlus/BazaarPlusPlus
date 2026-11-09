from datetime import date
from pathlib import Path

import pytest

from bazaarlens.config import ConfigurationError, load_config


def _write_env(root: Path, extra: str = "") -> None:
    (root / ".env").write_text("BPP_BUNDLE_SYNC_TOKEN=test-sync-token\n" + extra)


def test_defaults_need_only_credentials_and_do_not_create_state(
    tmp_path: Path, monkeypatch
) -> None:
    monkeypatch.setattr(Path, "home", lambda: tmp_path / "home")
    _write_env(
        tmp_path, "CLOUDFLARE_ACCOUNT_ID=fixture-account\nCLOUDFLARE_API_TOKEN=fixture-token\n"
    )
    config = load_config(root=tmp_path, require_object_store=True)
    assert config.data_root == tmp_path / "home/.local/share/bazaarplusplus/analyzer"
    assert not config.data_root.exists()
    assert config.api_base_url == "https://mod-api-v6.bazaarplusplus.com"
    assert config.source_epoch == date(2026, 10, 30)
    assert config.r2_bucket == "bazaarplusplus-public"
    assert config.r2_prefix == "metrics/v6"
    assert config.bundle_retention_days == config.fact_retention_days == 8
    assert config.download_concurrency == 64
    assert config.download_lookahead == 128
    assert config.max_run_seconds == 21600
    assert config.cloudflare_account_id == "fixture-account"
    assert "fixture-token" not in repr(config)
    assert "test-sync-token" not in repr(config)


def test_ci_can_override_state_and_resource_limits(tmp_path: Path) -> None:
    _write_env(
        tmp_path,
        f"BPP_DATA_ROOT={tmp_path}/ci-state\nBPP_MAX_RUN_SECONDS=18000\n"
        "BPP_DUCKDB_THREADS=4\nBPP_DUCKDB_MEMORY_LIMIT=4GB\n",
    )
    config = load_config(root=tmp_path)
    assert config.data_root == tmp_path / "ci-state"
    assert config.max_run_seconds == 18000
    assert config.duckdb_threads == 4
    assert config.duckdb_memory_limit == "4GB"


@pytest.mark.parametrize(
    "extra", ("", "CLOUDFLARE_ACCOUNT_ID=fixture-account\n", "CLOUDFLARE_API_TOKEN=fixture-token\n")
)
def test_publication_requires_both_credentials(tmp_path: Path, extra: str) -> None:
    _write_env(tmp_path, extra)
    with pytest.raises(ConfigurationError, match="object-store configuration"):
        load_config(root=tmp_path, require_object_store=True)
    load_config(root=tmp_path, require_object_store=False)


@pytest.mark.parametrize(
    "extra, message",
    (
        ("BPP_DATA_ROOT=relative\n", "must be absolute"),
        ("BPP_MAX_RUN_SECONDS=zero\n", "positive integer"),
        ("BPP_DUCKDB_THREADS=0\n", "positive integer"),
        ("BPP_DUCKDB_MEMORY_LIMIT=large\n", "positive size"),
        ("BPP_DUCKDB_MEMORY_LIMIT=0GB\n", "positive size"),
    ),
)
def test_invalid_overrides_are_rejected(tmp_path: Path, extra: str, message: str) -> None:
    _write_env(tmp_path, extra)
    with pytest.raises(ConfigurationError, match=message):
        load_config(root=tmp_path)


@pytest.mark.parametrize(
    "key",
    (
        "BPP_API_BASE_URL",
        "BPP_SOURCE_EPOCH",
        "BPP_BUNDLE_RETENTION_DAYS",
        "BPP_FACT_RETENTION_DAYS",
        "BPP_DOWNLOAD_CONCURRENCY",
        "BPP_DOWNLOAD_LOOKAHEAD",
        "BPP_METRICS_R2_BUCKET",
        "BPP_METRICS_R2_PREFIX",
    ),
)
def test_retired_settings_fail_instead_of_silently_changing_target(
    tmp_path: Path, key: str
) -> None:
    _write_env(tmp_path, f"{key}=private-value\n")
    with pytest.raises(ConfigurationError, match=key) as error:
        load_config(root=tmp_path)
    assert "private-value" not in str(error.value)


def test_missing_credentials_cannot_fall_back_to_environment(tmp_path: Path, monkeypatch) -> None:
    with pytest.raises(ConfigurationError, match=r"\.env"):
        load_config(root=tmp_path)
    (tmp_path / ".env").write_text("")
    monkeypatch.setenv("BPP_BUNDLE_SYNC_TOKEN", "ambient-token")
    with pytest.raises(ConfigurationError, match="Bundle Server"):
        load_config(root=tmp_path)
    load_config(root=tmp_path, require_source=False)
