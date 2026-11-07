"""Select a sealed Analysis Window and open its facts in DuckDB."""

from collections.abc import Generator, Iterable
from contextlib import contextmanager
from dataclasses import dataclass
from datetime import date, timedelta
from typing import Any

import duckdb

from bazaarlens.facts.admission import LEGEND_RANK
from bazaarlens.facts.store import DaySeal, FactStore, parse_source_day

ANALYSIS_DAYS = 7


class AnalysisWindowError(RuntimeError):
    """Complete Source Day seals cannot form an Analysis Window."""


@dataclass(frozen=True, slots=True)
class AnalysisWindow:
    seals: tuple[DaySeal, ...]
    start: date
    end: date

    @property
    def value(self) -> dict[str, object]:
        return {
            "start": self.start.isoformat(),
            "end": self.end.isoformat(),
            "days": len(self.seals),
        }


@dataclass(frozen=True, slots=True)
class FactStats:
    raw_runs: int
    discarded_unknown_hero: int
    discarded_unknown_final_rank: int
    included_runs: int
    included_battles: int


def select_analysis_window(
    seals: Iterable[DaySeal],
    anchor_day: date | str | None = None,
    *,
    source_epoch: date | str | None = None,
) -> AnalysisWindow | None:
    """Return one to seven latest consecutive verified day seals."""
    epoch = parse_source_day(source_epoch) if source_epoch is not None else None
    by_day: dict[date, DaySeal] = {}
    for seal in seals:
        day = parse_source_day(seal.source_day)
        if epoch is not None and day < epoch:
            continue
        if day in by_day:
            raise AnalysisWindowError(f"Duplicate Complete Source Day: {day.isoformat()}")
        by_day[day] = seal
    if not by_day:
        return None
    anchor = parse_source_day(anchor_day) if anchor_day is not None else max(by_day)
    if (epoch is not None and anchor < epoch) or anchor not in by_day:
        return None
    descending = []
    for offset in range(ANALYSIS_DAYS):
        candidate = anchor - timedelta(days=offset)
        if candidate not in by_day:
            break
        descending.append(candidate)
    days = tuple(reversed(descending))
    selected = tuple(by_day[day] for day in days)
    return AnalysisWindow(selected, days[0], days[-1])


@dataclass(frozen=True, slots=True)
class AnalysisResources:
    memory_limit: str
    threads: int


@contextmanager
def open_analysis(
    store: FactStore, window: AnalysisWindow, tables: tuple[str, ...], resources: AnalysisResources
) -> Generator[duckdb.DuckDBPyConnection]:
    """Resolve paths from checked seals; each caller owns one independent connection."""
    if (
        not isinstance(resources.threads, int)
        or isinstance(resources.threads, bool)
        or resources.threads < 1
    ):
        raise ValueError("DuckDB threads must be a positive integer")
    connection = duckdb.connect(database=":memory:")
    try:
        connection.execute(f"SET memory_limit={sql_string(resources.memory_limit)}")
        connection.execute(f"SET threads={resources.threads}")
        temporary = store.root / "duckdb-tmp"
        temporary.mkdir(parents=True, exist_ok=True)
        connection.execute(f"SET temp_directory={sql_string(str(temporary))}")
        paths = store.hour_paths(seal.source_day for seal in window.seals)
        for table in tables:
            explicit = ",".join(sql_string(str(path)) for path in paths[table])
            connection.execute(
                f"CREATE TEMP VIEW {table}_fact AS "
                f"SELECT * FROM read_parquet([{explicit}], hive_partitioning=false)"
            )
        if "runs" in tables:
            connection.execute(
                "CREATE TEMP VIEW completed_runs AS SELECT *, "
                f"CASE WHEN final_rank={sql_string(LEGEND_RANK)} THEN 'legend' "
                "ELSE 'non_legend' END AS segment "
                "FROM runs_fact WHERE lower(trim(status))='completed'"
            )
        yield connection
    finally:
        connection.close()


def fact_stats(store: FactStore, window: AnalysisWindow, resources: AnalysisResources) -> FactStats:
    with open_analysis(store, window, ("runs", "battles", "quarantine"), resources) as connection:
        included_runs = int(
            required_row(
                connection.execute("SELECT count(*) FROM runs_fact").fetchone(),
                "Included Run count",
            )[0]
        )
        discarded = required_row(
            connection.execute(
                """
            SELECT count(*) FILTER (WHERE coalesce(raw_run, false)),
                   count(*) FILTER (WHERE coalesce(discarded_unknown_hero, false)),
                   count(*) FILTER (WHERE coalesce(discarded_unknown_final_rank, false))
            FROM quarantine_fact
            """
            ).fetchone(),
            "Discarded Run counts",
        )
        included_battles = int(
            required_row(
                connection.execute(
                    """
                    SELECT count(*) FROM battles_fact b
                    JOIN runs_fact r
                      ON r.bundle_id=b.bundle_id AND r.run_id=b.run_id
                    """
                ).fetchone(),
                "Included Battle count",
            )[0]
        )

    return FactStats(
        raw_runs=included_runs + int(discarded[0]),
        discarded_unknown_hero=int(discarded[1]),
        discarded_unknown_final_rank=int(discarded[2]),
        included_runs=included_runs,
        included_battles=included_battles,
    )


def sql_string(value: str) -> str:
    return "'" + value.replace("'", "''") + "'"


def sql_list(values: Iterable[str]) -> str:
    return ",".join(sql_string(value) for value in values)


def required_row(value: tuple[Any, ...] | None, label: str) -> tuple[Any, ...]:
    if value is None:
        raise ValueError(f"{label} query returned no row")
    return value
