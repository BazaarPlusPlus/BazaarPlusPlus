"""Strict repository ``.env`` configuration without ambient-env fallback."""

import re
from dataclasses import dataclass, field
from datetime import date
from pathlib import Path

from dotenv import dotenv_values


class ConfigurationError(ValueError):
    """Required pipeline configuration is missing or invalid."""


MIN_FACT_RETENTION_DAYS = 8


@dataclass(frozen=True, slots=True)
class Config:
    data_root: Path
    api_base_url: str | None
    sync_token: str | None = field(repr=False)
    source_epoch: date | None = None
    bundle_retention_days: int = 8
    fact_retention_days: int = MIN_FACT_RETENTION_DAYS
    download_concurrency: int = 64
    download_lookahead: int = 128
    max_run_seconds: int = 21600
    duckdb_memory_limit: str = "8GB"
    duckdb_threads: int = 8
    r2_account_id: str | None = None
    r2_bucket: str | None = None
    r2_access_key_id: str | None = field(default=None, repr=False)
    r2_secret_access_key: str | None = field(default=None, repr=False)


def repository_root() -> Path:
    return Path(__file__).resolve().parents[2]


def load_config(
    *,
    require_source: bool = True,
    require_object_store: bool = False,
    root: Path | None = None,
) -> Config:
    repo = (root or repository_root()).resolve()
    env_file = repo / ".env"
    if not env_file.is_file():
        raise ConfigurationError("Repository .env file is missing")
    values = dotenv_values(env_file, interpolate=False)
    data_root_value = values.get("BPP_DATA_ROOT")
    if not isinstance(data_root_value, str) or not data_root_value.strip():
        raise ConfigurationError("BPP_DATA_ROOT is missing from repository .env")
    data_root = Path(data_root_value)
    if not data_root.is_absolute():
        data_root = repo / data_root
    api_base_url = _optional(values.get("BPP_V5_API_BASE_URL"))
    sync_token = _optional(values.get("BPP_BUNDLE_SYNC_TOKEN"))
    if require_source and (api_base_url is None or sync_token is None):
        raise ConfigurationError("Bundle Server configuration is incomplete")
    r2_account_id = _optional(values.get("BPP_METRICS_R2_ACCOUNT_ID"))
    r2_bucket = _optional(values.get("BPP_METRICS_R2_BUCKET"))
    r2_access_key_id = _optional(values.get("BPP_METRICS_R2_ACCESS_KEY_ID"))
    r2_secret_access_key = _optional(values.get("BPP_METRICS_R2_SECRET_ACCESS_KEY"))
    if require_object_store and not all(
        (r2_account_id, r2_bucket, r2_access_key_id, r2_secret_access_key)
    ):
        raise ConfigurationError("R2 object-store configuration is incomplete")
    return Config(
        data_root=data_root,
        api_base_url=api_base_url,
        sync_token=sync_token,
        source_epoch=_source_epoch(values.get("BPP_SOURCE_EPOCH")),
        bundle_retention_days=_positive_int(
            values.get("BPP_BUNDLE_RETENTION_DAYS"), 8, "BPP_BUNDLE_RETENTION_DAYS"
        ),
        fact_retention_days=_minimum_int(
            values.get("BPP_FACT_RETENTION_DAYS"),
            MIN_FACT_RETENTION_DAYS,
            "BPP_FACT_RETENTION_DAYS",
        ),
        download_concurrency=_positive_int(
            values.get("BPP_DOWNLOAD_CONCURRENCY"), 64, "BPP_DOWNLOAD_CONCURRENCY"
        ),
        download_lookahead=_positive_int(
            values.get("BPP_DOWNLOAD_LOOKAHEAD"), 128, "BPP_DOWNLOAD_LOOKAHEAD"
        ),
        max_run_seconds=_positive_int(
            values.get("BPP_MAX_RUN_SECONDS"), 21600, "BPP_MAX_RUN_SECONDS"
        ),
        duckdb_memory_limit=_memory_limit(values.get("BPP_DUCKDB_MEMORY_LIMIT")),
        duckdb_threads=_positive_int(values.get("BPP_DUCKDB_THREADS"), 8, "BPP_DUCKDB_THREADS"),
        r2_account_id=r2_account_id,
        r2_bucket=r2_bucket,
        r2_access_key_id=r2_access_key_id,
        r2_secret_access_key=r2_secret_access_key,
    )


def _optional(value: object) -> str | None:
    return value.strip() if isinstance(value, str) and value.strip() else None


def _positive_int(value: object, default: int, name: str) -> int:
    if value is None or value == "":
        return default
    try:
        parsed = int(str(value))
    except ValueError as error:
        raise ConfigurationError(f"{name} must be a positive integer") from error
    if parsed < 1:
        raise ConfigurationError(f"{name} must be a positive integer")
    return parsed


def _minimum_int(value: object, minimum: int, name: str) -> int:
    parsed = _positive_int(value, minimum, name)
    if parsed < minimum:
        raise ConfigurationError(f"{name} must be at least {minimum}")
    return parsed


def _source_epoch(value: object) -> date | None:
    parsed = _optional(value)
    if parsed is None:
        return None
    try:
        result = date.fromisoformat(parsed)
    except ValueError as error:
        raise ConfigurationError("BPP_SOURCE_EPOCH must use YYYY-MM-DD") from error
    if result.isoformat() != parsed:
        raise ConfigurationError("BPP_SOURCE_EPOCH must use YYYY-MM-DD")
    return result


def _memory_limit(value: object) -> str:
    if value is None or value == "":
        return "8GB"
    parsed = str(value).strip().upper()
    if re.fullmatch(r"\d+(?:\.\d+)?(?:KB|MB|GB|TB)", parsed) is None:
        raise ConfigurationError("BPP_DUCKDB_MEMORY_LIMIT must be a positive size such as 8GB")
    if float(parsed[:-2]) <= 0:
        raise ConfigurationError("BPP_DUCKDB_MEMORY_LIMIT must be a positive size such as 8GB")
    return parsed
