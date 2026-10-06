from datetime import date
from pathlib import Path

import pytest

from bppanalyzer.config import ConfigurationError, load_config


def _write_env(root: Path, extra: str = "") -> None:
    (root / ".env").write_text(
        "BPP_DATA_ROOT=fixture-data\n"
        "BPP_V5_API_BASE_URL=https://api.invalid\n"
        "BPP_BUNDLE_SYNC_TOKEN=fixture-sync-token\n"
        f"{extra}"
    )


def test_object_store_configuration_is_complete_and_credentials_are_repr_safe(
    tmp_path: Path,
) -> None:
    _write_env(
        tmp_path,
        "BPP_METRICS_R2_BUCKET=fixture-bucket\n"
        "CLOUDFLARE_ACCOUNT_ID=fixture-account\n"
        "CLOUDFLARE_API_TOKEN=fixture-token\n",
    )

    config = load_config(root=tmp_path, require_object_store=True)

    assert config.data_root == tmp_path / "fixture-data"
    assert config.r2_bucket == "fixture-bucket"
    assert config.cloudflare_account_id == "fixture-account"
    assert config.cloudflare_api_token == "fixture-token"
    assert "fixture-token" not in repr(config)


@pytest.mark.parametrize(
    "extra",
    (
        "BPP_METRICS_R2_BUCKET=fixture-bucket\n",
        "BPP_METRICS_R2_BUCKET=fixture-bucket\nCLOUDFLARE_ACCOUNT_ID=fixture-account\n",
        "CLOUDFLARE_ACCOUNT_ID=fixture-account\nCLOUDFLARE_API_TOKEN=fixture-token\n",
        # The earlier layout's derived pair is not a token.
        "BPP_METRICS_R2_BUCKET=fixture-bucket\nBPP_METRICS_R2_ACCOUNT_ID=a\n"
        "BPP_METRICS_R2_ACCESS_KEY_ID=k\nBPP_METRICS_R2_SECRET_ACCESS_KEY=s\n",
    ),
)
def test_object_store_configuration_rejects_partial_r2_credentials(
    tmp_path: Path, extra: str
) -> None:
    _write_env(tmp_path, extra)

    with pytest.raises(ConfigurationError, match="object-store configuration"):
        load_config(root=tmp_path, require_object_store=True)


@pytest.mark.parametrize(
    ("extra", "message"),
    (
        ("BPP_DOWNLOAD_CONCURRENCY=zero\n", "positive integer"),
        ("BPP_DUCKDB_THREADS=0\n", "positive integer"),
        ("BPP_DUCKDB_MEMORY_LIMIT=large\n", "positive size"),
        ("BPP_DUCKDB_MEMORY_LIMIT=0GB\n", "positive size"),
    ),
)
def test_numeric_and_memory_configuration_is_strict(
    tmp_path: Path, extra: str, message: str
) -> None:
    _write_env(tmp_path, extra)

    with pytest.raises(ConfigurationError, match=message):
        load_config(root=tmp_path)


def test_missing_env_and_source_configuration_are_rejected(tmp_path: Path) -> None:
    with pytest.raises(ConfigurationError, match=r"\.env"):
        load_config(root=tmp_path)
    (tmp_path / ".env").write_text("BPP_DATA_ROOT=data\n")
    with pytest.raises(ConfigurationError, match="Bundle Server"):
        load_config(root=tmp_path)


def test_source_epoch_is_optional_and_parsed_as_a_strict_utc_date(tmp_path: Path) -> None:
    _write_env(tmp_path)
    config = load_config(root=tmp_path)
    assert config.source_epoch is None
    assert config.bundle_retention_days == 8
    assert config.download_concurrency == 64
    assert config.download_lookahead == 128
    assert config.max_run_seconds == 21600

    _write_env(tmp_path, "BPP_SOURCE_EPOCH=2026-08-07\n")
    assert load_config(root=tmp_path).source_epoch == date(2026, 8, 7)

    _write_env(tmp_path, "BPP_SOURCE_EPOCH=2026-8-7\n")
    with pytest.raises(ConfigurationError, match=r"BPP_SOURCE_EPOCH.*YYYY-MM-DD"):
        load_config(root=tmp_path)


def test_fact_retention_defaults_to_eight_and_rejects_shorter_windows(tmp_path: Path) -> None:
    _write_env(tmp_path)
    assert load_config(root=tmp_path).fact_retention_days == 8

    _write_env(tmp_path, "BPP_FACT_RETENTION_DAYS=12\n")
    assert load_config(root=tmp_path).fact_retention_days == 12

    _write_env(tmp_path, "BPP_FACT_RETENTION_DAYS=7\n")
    with pytest.raises(ConfigurationError, match="BPP_FACT_RETENTION_DAYS must be at least 8"):
        load_config(root=tmp_path)
