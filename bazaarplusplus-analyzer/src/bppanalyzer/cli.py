"""Operator CLI for healing, two-product publication, status, and verification."""

import json
from datetime import datetime

import click

from bppanalyzer.bundle_source import BundleSource, RawHourIndex
from bppanalyzer.config import ConfigurationError, load_config
from bppanalyzer.driver import PipelineDriver
from bppanalyzer.fact_store import FactStore, FactStoreError, parse_source_day
from bppanalyzer.locking import LockHeld, LockOwnershipLost
from bppanalyzer.object_store import ObjectStoreError, R2ObjectStore
from bppanalyzer.operational_evidence import read_status
from bppanalyzer.publication import ContractViolation, PublicationError, validate_snapshot


@click.group()
def main() -> None:
    """bpp — Analyzer V5 pipeline."""


@main.command("run")
@click.option("--heal-days", type=click.IntRange(min=1), default=8, show_default=True)
@click.option("--anchor-day", type=str)
@click.option("--no-publish", is_flag=True, help="Build local snapshots without publishing.")
@click.option("--dry-run", is_flag=True, help="Report without reading or writing external state.")
@click.option("--quiet", is_flag=True, help="Suppress progress; keep the final summary.")
def run_command(
    heal_days: int,
    anchor_day: str | None,
    no_publish: bool,
    dry_run: bool,
    quiet: bool,
) -> None:
    """Heal Source Hours, seal complete days, and update current snapshots."""
    try:
        if anchor_day is not None:
            parse_source_day(anchor_day)
        config = load_config(
            require_source=not dry_run,
            require_object_store=not no_publish and not dry_run,
        )
        if dry_run:
            summary = PipelineDriver(
                config.data_root,
                source=_DryRunSource(),
                source_epoch=config.source_epoch,
            ).run(
                heal_days=heal_days,
                anchor_day=anchor_day,
                publish=not no_publish,
                dry_run=True,
            )
        else:
            assert config.api_base_url is not None and config.sync_token is not None
            with BundleSource(
                api_base_url=config.api_base_url,
                sync_token=config.sync_token,
                retention_days=config.bundle_retention_days,
                download_concurrency=config.download_concurrency,
                lookahead=config.download_lookahead,
            ) as source:
                summary = PipelineDriver(
                    config.data_root,
                    source=source,
                    source_epoch=config.source_epoch,
                    max_run_seconds=config.max_run_seconds,
                    duckdb_memory_limit=config.duckdb_memory_limit,
                    duckdb_threads=config.duckdb_threads,
                    fact_retention_days=config.fact_retention_days,
                    object_store=None if no_publish else _object_store(config),
                ).run(
                    heal_days=heal_days,
                    anchor_day=anchor_day,
                    publish=not no_publish,
                    progress_callback=None if quiet else click.echo,
                    error_callback=lambda message: click.echo(message, err=True),
                )
        click.echo(json.dumps(summary.report, sort_keys=True, separators=(",", ":")))
        click.echo(
            f"{summary.outcome}: {summary.hours_ingested} hours ingested, "
            f"{summary.days_sealed} days sealed, "
            f"{summary.days_abandoned} days abandoned"
        )
        if summary.exit_code:
            raise click.exceptions.Exit(summary.exit_code)
    except LockHeld:
        raise click.exceptions.Exit(3) from None
    except (ConfigurationError, ValueError) as error:
        raise click.UsageError(str(error)) from None
    except LockOwnershipLost as error:
        raise click.ClickException(str(error)) from None
    except (PublicationError, ObjectStoreError) as error:
        raise click.ClickException(str(error)) from None


@main.command("status")
@click.option("json_output", "--json", is_flag=True, help="Emit machine-readable JSON.")
def status_command(json_output: bool) -> None:
    """Show the last atomic local health snapshot."""
    try:
        config = load_config(require_source=False)
        value = read_status(config.data_root, source_epoch=config.source_epoch)
    except ConfigurationError as error:
        raise click.UsageError(str(error)) from None
    except Exception as error:
        raise click.ClickException(str(error)) from None
    if json_output:
        click.echo(json.dumps(value, sort_keys=True, separators=(",", ":")))
        return
    facts = value["facts"]
    last = value.get("last_run")
    click.echo(f"newest sealed day: {facts.get('newest_sealed_day') or '-'}")
    click.echo(f"abandoned days: {len(facts.get('abandoned_days', []))}")
    current = value.get("current_run")
    if isinstance(current, dict):
        click.echo(
            f"current run: {current.get('run_id') or '-'} "
            f"phase={current.get('phase') or '-'} step={current.get('step') or '-'} "
            f"hour={current.get('current_hour') or '-'} "
            f"hours={current.get('hours_done', 0)}/{current.get('hours_planned', 0)}"
        )
    click.echo(f"last outcome: {last.get('outcome') if isinstance(last, dict) else '-'}")


@main.command("verify")
@click.option("--day", type=str, help="Verify one Source Day (YYYY-MM-DD).")
@click.option("--deep", is_flag=True, help="Also validate local consumer snapshots.")
def verify_command(day: str | None, deep: bool) -> None:
    """Re-hash committed Parquet and optionally validate local snapshots."""
    try:
        if day is not None:
            parse_source_day(day)
        config = load_config(require_source=False)
        report = FactStore(config.data_root).verify(day, deep=deep)
        snapshots = _validate_local_snapshots(config.data_root) if deep else 0
    except (ConfigurationError, ValueError) as error:
        raise click.UsageError(str(error)) from None
    except (FactStoreError, ContractViolation) as error:
        raise click.ClickException(str(error)) from None
    suffix = f" and {snapshots} consumer snapshots" if deep else ""
    click.echo(
        f"verified {report.hours_verified} hours and {report.files_verified} Parquet files{suffix}"
    )


def _validate_local_snapshots(data_root) -> int:
    count = 0
    for product in ("heroes", "builds"):
        path = data_root / "snapshots" / product / "latest.json"
        if not path.is_file():
            continue
        value = json.loads(path.read_bytes())
        if not isinstance(value, dict):
            raise ContractViolation(f"Local {product} snapshot root must be an object")
        validate_snapshot(product, value)
        count += 1
    return count


def _object_store(config):
    assert config.r2_account_id is not None
    assert config.r2_bucket is not None
    assert config.r2_access_key_id is not None
    assert config.r2_secret_access_key is not None
    return R2ObjectStore(
        account_id=config.r2_account_id,
        bucket=config.r2_bucket,
        access_key_id=config.r2_access_key_id,
        secret_access_key=config.r2_secret_access_key,
    )


class _DryRunSource:
    def hour_index(self, source_hour: datetime) -> RawHourIndex:
        raise AssertionError("Dry run must not read the Bundle Server")

    def stream(self, index: RawHourIndex):
        raise AssertionError("Dry run must not read the Bundle Server")


if __name__ == "__main__":
    main()
