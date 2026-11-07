import json
from dataclasses import replace
from datetime import UTC, date, datetime, timedelta
from pathlib import Path

import pytest
from jsonschema import Draft202012Validator

from bazaarlens.analysis import AnalysisResources, AnalysisWindowError, select_analysis_window
from bazaarlens.facts.store import DaySeal, FactStore
from bazaarlens.locking import LockOwnershipLost, MaximumRunTimeExceeded
from bazaarlens.object_store import ObjectStoreError
from bazaarlens.products import BUILDS, HEROES
from bazaarlens.products.base import ContractViolation, ProductData
from bazaarlens.products.builds import BuildRank, select_build_identities
from bazaarlens.snapshots import LatestPublisher, SnapshotRunner
from tests.fakes import MemoryObjectStore


def _runner(store: FactStore, **kwargs) -> SnapshotRunner:
    return SnapshotRunner(store, AnalysisResources("1GB", 1), **kwargs)


def _seal(day: date) -> DaySeal:
    return DaySeal(day.isoformat(), (), {}, day.isoformat(), reused=True)


@pytest.mark.parametrize("count", (1, 5, 7))
def test_analysis_window_grows_from_one_to_seven_complete_source_days(count: int) -> None:
    start = date(2026, 8, 1)
    seals = tuple(_seal(start + timedelta(days=offset)) for offset in range(count))

    selected = select_analysis_window(seals)

    assert selected is not None
    assert selected.start == start
    assert selected.end == start + timedelta(days=count - 1)
    assert len(selected.seals) == count
    assert selected.value["days"] == count


def test_analysis_window_keeps_only_the_latest_seven_consecutive_days() -> None:
    start = date(2026, 8, 1)
    seals = tuple(_seal(start + timedelta(days=offset)) for offset in range(9))

    selected = select_analysis_window(seals)

    assert selected is not None
    assert selected.start == date(2026, 8, 3)
    assert selected.end == date(2026, 8, 9)
    assert len(selected.seals) == 7
    with pytest.raises(AnalysisWindowError, match="Duplicate"):
        select_analysis_window((*seals, seals[0]))


def test_explicit_window_anchor_uses_the_available_consecutive_suffix() -> None:
    start = date(2026, 8, 1)
    seals = tuple(_seal(start + timedelta(days=offset)) for offset in range(8))

    selected = select_analysis_window(seals, start + timedelta(days=5))
    assert selected is not None
    assert selected.start == start
    assert selected.end == start + timedelta(days=5)
    assert selected.value["days"] == 6

    selected = select_analysis_window(seals, start + timedelta(days=7))
    assert selected is not None
    assert selected.start == start + timedelta(days=1)
    assert selected.end == start + timedelta(days=7)


def test_analysis_window_never_considers_seals_before_the_source_epoch() -> None:
    start = date(2026, 8, 1)
    seals = tuple(_seal(start + timedelta(days=offset)) for offset in range(9))

    selected = select_analysis_window(seals, source_epoch=date(2026, 8, 7))

    assert selected is not None
    assert selected.start == date(2026, 8, 7)
    assert selected.end == date(2026, 8, 9)
    assert selected.value["days"] == 3
    assert (
        select_analysis_window(
            seals,
            anchor_day=date(2026, 8, 6),
            source_epoch=date(2026, 8, 7),
        )
        is None
    )


def test_heroes_schema_requires_days_length_to_equal_window_days(
    canonical_fact_store,
) -> None:
    _root, store = canonical_fact_store
    window = select_analysis_window(store.seals())
    assert window is not None
    payload = json.loads(_runner(store).build(HEROES, window).content)
    payload["window"]["days"] = 6
    schema = json.loads(Path("contracts/v6/heroes.schema.json").read_bytes())

    assert list(Draft202012Validator(schema).iter_errors(payload))


def test_latest_publisher_only_writes_the_two_public_keys(canonical_fact_store) -> None:
    _root, store = canonical_fact_store
    window = select_analysis_window(store.seals())
    assert window is not None
    runner = _runner(store)
    heroes = runner.build(HEROES, window)
    builds = runner.build(BUILDS, window)
    objects = MemoryObjectStore()
    publisher = LatestPublisher(objects)

    assert publisher.replace(HEROES, heroes) is True
    assert publisher.replace(BUILDS, builds) is True

    assert objects.put_keys() == [
        HEROES.public_key,
        BUILDS.public_key,
    ]
    for key in (HEROES.public_key, BUILDS.public_key):
        observed = objects.get(key)
        assert observed is not None
        assert observed.stat.cache_control == "public,max-age=60,must-revalidate"
        assert observed.stat.content_type == "application/json"


def test_product_validation_failure_preserves_old_object_and_does_not_block_other_product(
    nine_sealed_days: Path,
) -> None:
    store = FactStore(nine_sealed_days)
    window = select_analysis_window(store.seals())
    assert window is not None
    previous = b'{"old":"heroes"}\n'
    local = store.root / HEROES.local_relpath
    local.parent.mkdir(parents=True, exist_ok=True)
    local.write_bytes(previous)
    objects = MemoryObjectStore()
    objects.put(
        HEROES.public_key,
        previous,
        cache_control="public,max-age=60,must-revalidate",
        content_type="application/json",
    )
    invalid_heroes = replace(
        HEROES, build=lambda *_args: ProductData({"invalid": True}, HEROES.empty_counts())
    )
    runner = _runner(store, object_store=objects)

    failed = runner.ensure(invalid_heroes, window)
    succeeded = runner.ensure(BUILDS, window)

    assert isinstance(failed.error, ContractViolation)
    assert not failed.local_saved and not failed.remote_confirmed
    assert failed.phase == "build"
    assert local.read_bytes() == previous
    assert objects.get(HEROES.public_key).body == previous
    assert succeeded.error is None and succeeded.remote_confirmed
    assert objects.get(BUILDS.public_key).body == (store.root / BUILDS.local_relpath).read_bytes()


def test_identical_snapshot_is_remotely_confirmed_without_another_upload(
    nine_sealed_days: Path,
) -> None:
    store = FactStore(nine_sealed_days)
    window = select_analysis_window(store.seals())
    assert window is not None
    objects = MemoryObjectStore()
    runner = _runner(store, object_store=objects, clock=lambda: datetime(2026, 8, 16, tzinfo=UTC))

    first = runner.ensure(HEROES, window)
    repeated = runner.ensure(HEROES, window)

    assert first.error is None and first.uploaded
    assert repeated.error is None and not repeated.uploaded
    assert first.remote_confirmed and repeated.remote_confirmed
    assert first.local_saved and repeated.local_saved
    assert repeated.phase == "complete"
    assert first.counts == repeated.counts
    assert objects.put_keys() == [HEROES.public_key]


def test_publication_failure_preserves_successful_local_save_and_counts(
    nine_sealed_days: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    store = FactStore(nine_sealed_days)
    window = select_analysis_window(store.seals())
    assert window is not None
    objects = MemoryObjectStore()
    failure = ObjectStoreError("fixture upload failed")

    def fail_put(*_args, **_kwargs):
        raise failure

    original_put = objects.put
    monkeypatch.setattr(objects, "put", fail_put)
    runner = _runner(store, object_store=objects)
    result = runner.ensure(HEROES, window)

    assert result.error is failure and result.phase == "publish"
    assert result.local_saved and not result.remote_confirmed and not result.uploaded
    assert result.counts != HEROES.empty_counts()
    assert json.loads((store.root / HEROES.local_relpath).read_bytes())["window"] == window.value
    assert objects.get(HEROES.public_key) is None
    monkeypatch.setattr(objects, "put", original_put)
    recovered = runner.ensure(HEROES, window)
    assert recovered.error is None and recovered.reused and recovered.uploaded
    assert recovered.counts == result.counts


def test_ownership_loss_stops_before_local_or_remote_replacement(nine_sealed_days: Path) -> None:
    store = FactStore(nine_sealed_days)
    window = select_analysis_window(store.seals())
    assert window is not None
    local = store.root / HEROES.local_relpath
    previous = b'{"old":"heroes"}\n'
    local.parent.mkdir(parents=True, exist_ok=True)
    local.write_bytes(previous)
    objects = MemoryObjectStore()

    def lost_ownership():
        raise LockOwnershipLost("fixture owner replaced")

    runner = _runner(store, object_store=objects, ownership_check=lost_ownership)
    with pytest.raises(LockOwnershipLost, match="fixture owner replaced"):
        runner.ensure(HEROES, window)

    assert local.read_bytes() == previous
    assert objects.requests == []


@pytest.mark.parametrize("failure_type", (ValueError, OSError))
def test_observer_failure_after_publication_is_fatal_and_preserves_completed_writes(
    nine_sealed_days: Path, failure_type: type[Exception]
) -> None:
    store = FactStore(nine_sealed_days)
    window = select_analysis_window(store.seals())
    assert window is not None
    objects = MemoryObjectStore()
    failure = failure_type("fixture observer failed")

    def observe(result):
        if result.remote_confirmed:
            raise failure

    with pytest.raises(failure_type) as raised:
        _runner(store, object_store=objects).ensure(HEROES, window, on_progress=observe)

    assert raised.value is failure
    assert objects.get(HEROES.public_key).body == (store.root / HEROES.local_relpath).read_bytes()


def test_top_500_then_coverage_appends_only_highest_ranked_uncovered_build() -> None:
    common = tuple(f"10000000-0000-0000-0000-{number:012d}" for number in range(500))
    rare = "ffffffff-ffff-ffff-ffff-ffffffffffff"
    candidates = [BuildRank((card_id,), score=100, ten_win=1, p75=10) for card_id in common]
    candidates.append(BuildRank((rare,), score=100, ten_win=1, p75=10))
    candidates.append(BuildRank((rare, rare), score=100, ten_win=1, p75=10))

    selected = select_build_identities(candidates)

    assert len(candidates) == 502
    assert len(selected) == 501
    containing = [identity for identity in selected if rare in identity]
    assert containing == [(rare,)]


@pytest.mark.parametrize("product", [HEROES, BUILDS], ids=lambda product: product.name)
def test_unchanged_inputs_reuse_bytes_across_clocks_and_recover_remote(
    nine_sealed_days: Path, monkeypatch: pytest.MonkeyPatch, product
) -> None:
    store = FactStore(nine_sealed_days)
    window = select_analysis_window(store.seals())
    assert window is not None
    product = replace(product, calculation_version="cache-test")
    objects = MemoryObjectStore()
    runner = _runner(store, object_store=objects, clock=lambda: datetime(2026, 8, 16, tzinfo=UTC))
    first = runner.ensure(product, window, publish=False)
    assert first.local_saved and not first.reused and not first.remote_confirmed
    path = store.root / product.local_relpath
    original = path.read_bytes()
    original_mtime = path.stat().st_mtime_ns

    def unexpected_build(*_args):
        raise AssertionError("Unchanged inputs must not open DuckDB or rebuild")

    monkeypatch.setattr(runner, "build", unexpected_build)
    runner.clock = lambda: datetime(2026, 8, 17, tzinfo=UTC)
    recovered = runner.ensure(product, window)
    assert recovered.error is None and recovered.reused and recovered.uploaded
    assert recovered.local_saved and recovered.remote_confirmed
    repeated = runner.ensure(product, window)
    assert repeated.error is None and repeated.reused and not repeated.uploaded
    assert repeated.counts == first.counts
    assert path.read_bytes() == original and path.stat().st_mtime_ns == original_mtime
    assert objects.put_keys() == [product.public_key]
    objects.objects.clear()
    repaired = runner.ensure(product, window)
    assert repaired.error is None and repaired.reused and repaired.uploaded
    assert objects.get(product.public_key).body == original


@pytest.mark.parametrize("changed", ["version", "contract", "seal", "window"])
def test_snapshot_rebuilds_when_an_input_identity_changes(
    nine_sealed_days: Path, monkeypatch: pytest.MonkeyPatch, changed: str
) -> None:
    from bazaarlens import snapshots

    store = FactStore(nine_sealed_days)
    window = select_analysis_window(store.seals())
    assert window is not None
    product = replace(HEROES, calculation_version="cache-test")
    runner = _runner(store)
    assert runner.ensure(product, window).error is None
    if changed == "version":
        product = replace(product, calculation_version="cache-test-2")
    elif changed == "contract":
        original_contract = snapshots.contract_bytes

        def changed_contract(name):
            payload = json.loads(original_contract(name))
            if name == product.schema_file:
                payload["description"] = "A changed contract identity"
            return json.dumps(payload).encode()

        monkeypatch.setattr(snapshots, "contract_bytes", changed_contract)
    elif changed == "seal":
        window = replace(
            window, seals=(replace(window.seals[0], day_seal_sha256="f" * 64), *window.seals[1:])
        )
    else:
        window = select_analysis_window(store.seals(), window.end - timedelta(days=1))
        assert window is not None
    result = runner.ensure(product, window)
    assert result.error is None and result.local_saved and not result.reused


@pytest.mark.parametrize("damage", ["missing", "malformed", "digest", "counts", "payload"])
def test_unusable_local_cache_rebuilds_without_publishing_damaged_bytes(
    nine_sealed_days: Path, damage: str
) -> None:
    import hashlib

    store = FactStore(nine_sealed_days)
    window = select_analysis_window(store.seals())
    assert window is not None
    product = replace(HEROES, calculation_version="cache-test")
    objects = MemoryObjectStore()
    runner = _runner(store, object_store=objects)
    assert runner.ensure(product, window, publish=False).error is None
    path = store.root / product.local_relpath
    sidecar = path.with_suffix(".inputs.json")
    if damage == "missing":
        sidecar.unlink()
    elif damage == "malformed":
        sidecar.write_text("[]")
    elif damage == "digest":
        path.write_bytes(b"{}")
    else:
        evidence = json.loads(sidecar.read_bytes())
        if damage == "counts":
            evidence["counts"]["participating_runs"] = True
        else:
            path.write_bytes(b"{}")
            evidence["content_sha256"] = hashlib.sha256(b"{}").hexdigest()
        sidecar.write_text(json.dumps(evidence))
    result = runner.ensure(product, window)
    assert result.error is None and not result.reused and result.uploaded
    assert json.loads(objects.get(product.public_key).body)["window"] == window.value


def test_cache_hit_does_not_hide_missing_facts(nine_sealed_days: Path) -> None:
    from bazaarlens.facts.store import FactStoreError

    store = FactStore(nine_sealed_days)
    window = select_analysis_window(store.seals())
    assert window is not None
    runner = _runner(store)
    assert runner.ensure(HEROES, window).error is None
    paths = store.hour_paths(seal.source_day for seal in window.seals)
    paths["runs"][0].unlink()
    result = runner.ensure(HEROES, window)
    assert isinstance(result.error, FactStoreError)
    assert not result.local_saved and not result.remote_confirmed


def test_interrupted_sidecar_save_retains_local_evidence_and_recovers(
    nine_sealed_days: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    from bazaarlens import snapshots

    store = FactStore(nine_sealed_days)
    window = select_analysis_window(store.seals())
    assert window is not None
    product = replace(HEROES, calculation_version="cache-test")
    runner = _runner(store)
    original_replace = snapshots.atomic_replace

    def fail_sidecar(path, content, **kwargs):
        if path.name.endswith(".inputs.json"):
            raise OSError("fixture sidecar save failed")
        original_replace(path, content, **kwargs)

    with monkeypatch.context() as patch:
        patch.setattr(snapshots, "atomic_replace", fail_sidecar)
        result = runner.ensure(product, window)
    assert isinstance(result.error, OSError)
    assert result.local_saved and result.counts != product.empty_counts()
    assert not result.remote_confirmed
    recovered = runner.ensure(product, window)
    assert recovered.error is None and recovered.local_saved and not recovered.reused
    assert runner.ensure(product, window).reused


@pytest.mark.parametrize("error_type", [LockOwnershipLost, MaximumRunTimeExceeded])
def test_cached_snapshot_still_honors_ownership_and_runtime_budget(
    nine_sealed_days: Path, error_type
) -> None:
    store = FactStore(nine_sealed_days)
    window = select_analysis_window(store.seals())
    assert window is not None
    objects = MemoryObjectStore()
    runner = _runner(store, object_store=objects)
    assert runner.ensure(HEROES, window, publish=False).error is None
    path = store.root / HEROES.local_relpath
    before = path.stat().st_mtime_ns

    def stopped():
        raise error_type("fixture run stopped")

    runner.ownership_check = stopped
    with pytest.raises(error_type):
        runner.ensure(HEROES, window)
    assert path.stat().st_mtime_ns == before
    assert not objects.requests
