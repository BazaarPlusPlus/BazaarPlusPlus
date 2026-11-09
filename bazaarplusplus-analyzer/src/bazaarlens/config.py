"""Strict repository ``.env`` configuration without ambient-env fallback."""

import re
from dataclasses import dataclass, field
from datetime import date
from pathlib import Path

from dotenv import dotenv_values

from bazaarlens.bundle_source import (
    API_BASE_URL,
    BUNDLE_RETENTION_DAYS,
    DOWNLOAD_CONCURRENCY,
    DOWNLOAD_LOOKAHEAD,
    SOURCE_EPOCH,
)
from bazaarlens.object_store import METRICS_BUCKET, METRICS_PREFIX


class ConfigurationError(ValueError):
    """Required pipeline configuration is missing or invalid."""


MIN_FACT_RETENTION_DAYS = 8


@dataclass(frozen=True, slots=True)
class Config:
    data_root: Path
    api_base_url: str | None
    sync_token: str | None = field(repr=False)
    source_epoch: date | None = None
    bundle_retention_days: int = BUNDLE_RETENTION_DAYS
    fact_retention_days: int = MIN_FACT_RETENTION_DAYS
    download_concurrency: int = DOWNLOAD_CONCURRENCY
    download_lookahead: int = DOWNLOAD_LOOKAHEAD
    max_run_seconds: int = 21600
    duckdb_memory_limit: str = "8GB"
    duckdb_threads: int = 8
    # One Cloudflare operator token serves every R2 bucket; the S3 pair the
    # object store needs is derived from it (object_store.derive_s3_credentials).
    cloudflare_account_id: str | None = None
    cloudflare_api_token: str | None = field(default=None, repr=False)
    r2_bucket: str | None = None
    r2_prefix: str = ""


def default_data_root() -> Path:
    return Path.home() / ".local" / "share" / "bazaarplusplus" / "analyzer"


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
    supported = {
        "BPP_DATA_ROOT",
        "BPP_MAX_RUN_SECONDS",
        "BPP_DUCKDB_MEMORY_LIMIT",
        "BPP_DUCKDB_THREADS",
        "BPP_BUNDLE_SYNC_TOKEN",
        "CLOUDFLARE_ACCOUNT_ID",
        "CLOUDFLARE_API_TOKEN",
    }
    unsupported = sorted(set(values) - supported)
    if unsupported:
        raise ConfigurationError("Unsupported analyzer settings: " + ", ".join(unsupported))
    data_root_value = _optional(values.get("BPP_DATA_ROOT"))
    data_root = Path(data_root_value) if data_root_value else default_data_root()
    if not data_root.is_absolute():
        raise ConfigurationError("BPP_DATA_ROOT must be absolute")
    sync_token = _optional(values.get("BPP_BUNDLE_SYNC_TOKEN"))
    if require_source and sync_token is None:
        raise ConfigurationError("Bundle Server configuration is incomplete")
    cloudflare_account_id = _optional(values.get("CLOUDFLARE_ACCOUNT_ID"))
    cloudflare_api_token = _optional(values.get("CLOUDFLARE_API_TOKEN"))
    if require_object_store and not all((cloudflare_account_id, cloudflare_api_token)):
        raise ConfigurationError("R2 object-store configuration is incomplete")
    return Config(
        data_root=data_root,
        api_base_url=API_BASE_URL,
        sync_token=sync_token,
        source_epoch=SOURCE_EPOCH,
        max_run_seconds=_positive_int(
            values.get("BPP_MAX_RUN_SECONDS"), 21600, "BPP_MAX_RUN_SECONDS"
        ),
        duckdb_memory_limit=_memory_limit(values.get("BPP_DUCKDB_MEMORY_LIMIT")),
        duckdb_threads=_positive_int(values.get("BPP_DUCKDB_THREADS"), 8, "BPP_DUCKDB_THREADS"),
        cloudflare_account_id=cloudflare_account_id,
        cloudflare_api_token=cloudflare_api_token,
        r2_bucket=METRICS_BUCKET,
        r2_prefix=METRICS_PREFIX,
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


def _memory_limit(value: object) -> str:
    if value is None or value == "":
        return "8GB"
    parsed = str(value).strip().upper()
    if re.fullmatch(r"\d+(?:\.\d+)?(?:KB|MB|GB|TB)", parsed) is None:
        raise ConfigurationError("BPP_DUCKDB_MEMORY_LIMIT must be a positive size such as 8GB")
    if float(parsed[:-2]) <= 0:
        raise ConfigurationError("BPP_DUCKDB_MEMORY_LIMIT must be a positive size such as 8GB")
    return parsed
