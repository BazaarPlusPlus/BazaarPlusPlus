"""Build, validate, save, and independently publish consumer snapshots."""

import hashlib
import json
import time
from collections.abc import Callable, Mapping
from datetime import UTC, datetime
from typing import Any

from jsonschema import Draft202012Validator

from bazaarlens.analysis import ANALYSIS_DAYS, AnalysisResources, AnalysisWindow, open_analysis
from bazaarlens.durable import atomic_replace, canonical_json
from bazaarlens.facts.store import FactStore, parse_source_day
from bazaarlens.locking import LockOwnershipLost
from bazaarlens.object_store import ObjectStore
from bazaarlens.products.base import (
    BuiltSnapshot,
    ConsumerProduct,
    ContractViolation,
    SnapshotResult,
)
from bazaarlens.resources import contract_bytes

LATEST_CACHE_CONTROL = "public,max-age=60,must-revalidate"
JSON_CONTENT_TYPE = "application/json"


class PublicationError(RuntimeError):
    """A consumer snapshot could not be published and confirmed."""


def validate_snapshot(product: ConsumerProduct, payload: Mapping[str, Any]) -> None:
    try:
        schema = json.loads(contract_bytes(product.schema_file))
        Draft202012Validator(schema).validate(payload)
    except Exception as error:
        raise ContractViolation(f"{product.name} snapshot failed JSON Schema validation") from error
    window = payload["window"]
    start = parse_source_day(window["start"])
    end = parse_source_day(window["end"])
    window_days = int(window["days"])
    if not 1 <= window_days <= ANALYSIS_DAYS or (end - start).days + 1 != window_days:
        raise ContractViolation("Analysis Window must contain one to seven consecutive days")
    product.validate_semantics(payload, start, end, window_days)


class LatestPublisher:
    def __init__(self, object_store: ObjectStore) -> None:
        self.object_store = object_store

    def replace(self, product: ConsumerProduct, snapshot: BuiltSnapshot) -> bool:
        if product.name != snapshot.product or snapshot.key != product.public_key:
            raise ContractViolation("Snapshot product and public key disagree")
        digest = hashlib.sha256(snapshot.content).hexdigest()
        observed = self.object_store.stat(snapshot.key)
        if (
            observed is not None
            and observed.sha256 == digest
            and observed.bytes == len(snapshot.content)
            and observed.cache_control == LATEST_CACHE_CONTROL
            and observed.content_type == JSON_CONTENT_TYPE
        ):
            return False
        self.object_store.put(
            snapshot.key,
            snapshot.content,
            cache_control=LATEST_CACHE_CONTROL,
            content_type=JSON_CONTENT_TYPE,
        )
        confirmed = self.object_store.get(snapshot.key)
        if (
            confirmed is None
            or confirmed.body != snapshot.content
            or confirmed.stat.cache_control != LATEST_CACHE_CONTROL
            or confirmed.stat.content_type != JSON_CONTENT_TYPE
        ):
            raise PublicationError(f"Published snapshot did not confirm: {snapshot.key}")
        return True


class SnapshotRunner:
    """One product's execution and progress, without knowing the product registry."""

    def __init__(
        self,
        store: FactStore,
        resources: AnalysisResources,
        *,
        clock: Callable[[], datetime] = lambda: datetime.now(UTC),
        object_store: ObjectStore | None = None,
        ownership_check: Callable[[], None] = lambda: None,
    ) -> None:
        self.store = store
        self.resources = resources
        self.clock = clock
        self.object_store = object_store
        self.ownership_check = ownership_check

    def build(self, product: ConsumerProduct, window: AnalysisWindow) -> BuiltSnapshot:
        """Compute validated canonical bytes without replacing any consumer snapshot."""
        with open_analysis(self.store, window, product.tables, self.resources) as connection:
            data = product.build(connection, window, self.clock())
        validate_snapshot(product, data.payload)
        if set(data.counts) != set(product.counter_names) or any(
            not isinstance(value, int) or isinstance(value, bool) or value < 0
            for value in data.counts.values()
        ):
            raise ContractViolation(f"Counters do not belong to consumer product: {product.name}")
        return BuiltSnapshot(
            product.name, product.public_key, canonical_json(data.payload), data.counts
        )

    def ensure(
        self,
        product: ConsumerProduct,
        window: AnalysisWindow,
        *,
        publish: bool = True,
        on_progress: Callable[[SnapshotResult], None] = lambda _result: None,
        on_message: Callable[[str], None] = lambda _message: None,
    ) -> SnapshotResult:
        """Keep product failures local; ownership and observer failures stop the Run.

        Observations follow successful durable stages, so later failure preserves
        completed local saves and remote publication in Operational Evidence.
        """
        result = SnapshotResult(product.name, product.empty_counts())
        observer_error: BaseException | None = None

        def observe(message: str) -> None:
            nonlocal observer_error
            try:
                on_progress(result)
                on_message(message)
            except BaseException as error:
                observer_error = error
                raise

        try:
            observe(f"{product.name} snapshot build started")
            started = time.monotonic()
            try:
                built = self.build(product, window)
            finally:
                result.timings[f"{product.name}_build_seconds"] = round(
                    time.monotonic() - started, 6
                )
            self.ownership_check()
            result.phase = "save"
            started = time.monotonic()
            try:
                atomic_replace(
                    self.store.root / product.local_relpath,
                    built.content,
                    ownership_check=self.ownership_check,
                )
            finally:
                result.timings[f"{product.name}_local_save_seconds"] = round(
                    time.monotonic() - started, 6
                )
            result.local_saved = True
            result.counts = built.counts
            observe(f"{product.name} snapshot build done")
            if publish and self.object_store is not None:
                result.phase = "publish"
                observe(f"{product.name} snapshot publication started")
                self.ownership_check()
                started = time.monotonic()
                try:
                    result.uploaded = LatestPublisher(self.object_store).replace(product, built)
                    result.remote_confirmed = True
                finally:
                    result.timings[f"{product.name}_publish_seconds"] = round(
                        time.monotonic() - started, 6
                    )
                result.phase = "complete"
                observe(
                    f"{product.name} snapshot publication done: replaced={str(result.uploaded).lower()}"
                )
            else:
                result.phase = "complete"
                reason = "--no-publish" if not publish else "no object store"
                observe(f"{product.name} snapshot publication skipped: {reason}")
        except LockOwnershipLost:
            raise
        except Exception as error:
            if error is observer_error:
                raise
            result.error = error
        return result
