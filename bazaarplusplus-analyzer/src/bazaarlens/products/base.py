"""Explicit consumer product definitions and their execution results."""

from collections.abc import Callable, Mapping
from dataclasses import dataclass, field
from datetime import date, datetime
from pathlib import Path
from typing import Any, Literal

import duckdb

from bazaarlens.analysis import AnalysisWindow


class ContractViolation(ValueError):
    """A consumer snapshot violates its schema or semantic invariants."""


@dataclass(frozen=True, slots=True)
class ProductData:
    payload: Mapping[str, Any]
    counts: Mapping[str, int]


@dataclass(frozen=True, slots=True)
class ConsumerProduct:
    name: str
    public_key: str
    schema_file: str
    local_relpath: Path
    tables: tuple[str, ...]
    build: Callable[[duckdb.DuckDBPyConnection, AnalysisWindow, datetime], ProductData]
    validate_semantics: Callable[[Mapping[str, Any], date, date, int], None]
    counter_names: tuple[str, ...]

    def empty_counts(self) -> dict[str, int]:
        return dict.fromkeys(self.counter_names, 0)


@dataclass(frozen=True, slots=True)
class BuiltSnapshot:
    product: str
    key: str
    content: bytes
    counts: Mapping[str, int]


@dataclass(slots=True)
class SnapshotResult:
    product: str
    counts: Mapping[str, int]
    phase: Literal["build", "save", "publish", "complete"] = "build"
    local_saved: bool = False
    remote_confirmed: bool = False
    uploaded: bool = False
    timings: dict[str, float] = field(default_factory=dict)
    error: Exception | None = None
