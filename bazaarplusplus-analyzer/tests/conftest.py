import shutil
from datetime import date
from pathlib import Path

import pytest

from bppanalyzer.fact_store import FactStore
from tests.pipeline_fixtures import heal_from_bundles

# Healing runs the production driver once per Source Day, so each sealed
# store is healed once per session and copied into the tests that use it.


@pytest.fixture(scope="session")
def _seven_sealed_days(tmp_path_factory: pytest.TempPathFactory) -> Path:
    root = tmp_path_factory.mktemp("seven-sealed-days") / "root"
    heal_from_bundles(root, 7)
    return root


@pytest.fixture(scope="session")
def _nine_sealed_days(tmp_path_factory: pytest.TempPathFactory, _seven_sealed_days: Path) -> Path:
    root = tmp_path_factory.mktemp("nine-sealed-days") / "root"
    shutil.copytree(_seven_sealed_days, root)
    heal_from_bundles(root, 2, start=date(2026, 8, 14))
    return root


@pytest.fixture(scope="session")
def canonical_fact_store(
    tmp_path_factory: pytest.TempPathFactory, _seven_sealed_days: Path
) -> tuple[Path, FactStore]:
    """Source Days 2026-08-07..13, shared by every test in the session."""
    root = tmp_path_factory.mktemp("canonical-facts") / "root"
    shutil.copytree(_seven_sealed_days, root)
    return root, FactStore(root)


@pytest.fixture
def nine_sealed_days(tmp_path: Path, _nine_sealed_days: Path) -> Path:
    """A private copy of Source Days 2026-08-07..15."""
    root = tmp_path / "facts"
    shutil.copytree(_nine_sealed_days, root)
    return root
