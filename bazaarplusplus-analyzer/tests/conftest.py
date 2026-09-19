from pathlib import Path

import pytest

from bppanalyzer.fact_store import FactStore
from tests.release_fixtures import sealed_store


@pytest.fixture(scope="session")
def canonical_fact_store(
    tmp_path_factory: pytest.TempPathFactory,
) -> tuple[Path, FactStore]:
    root = tmp_path_factory.mktemp("canonical-facts")
    return root, sealed_store(root, 7)
