"""Read the owned contracts from a checkout or an installed distribution."""

from pathlib import Path


def contract_bytes(name: str) -> bytes:
    package = Path(__file__).resolve().parent
    contracts = package / "contracts" / "v6"
    if not contracts.is_dir():
        contracts = package.parents[1] / "contracts" / "v6"
    return (contracts / name).read_bytes()
